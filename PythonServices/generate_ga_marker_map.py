import json
import sys
from pathlib import Path
from urllib.parse import urlparse

import cv2
from PIL import Image, ImageDraw, ImageFont

MIN_AUTO_MATCH_CONFIDENCE = 0.55


def resolve_web_path(web_path):
    value = str(web_path or "").strip()
    if not value:
        return None

    path = Path(value)
    if path.exists():
        return path

    # Windows absolute paths look like "C:\..." and urlparse treats "C"
    # as a URL scheme. Keep them as filesystem paths before URL handling.
    if len(value) > 2 and value[1] == ":":
        return path

    parsed = urlparse(value)
    if parsed.scheme or parsed.netloc:
        value = parsed.path

    path = Path(value)
    if path.exists():
        return path

    python_services_dir = Path(__file__).resolve().parent
    project_dir = python_services_dir.parent
    candidates = [
        project_dir / "wwwroot" / value.lstrip("/\\"),
        Path.cwd() / "wwwroot" / value.lstrip("/\\"),
        Path.cwd() / value.lstrip("/\\"),
    ]

    for candidate in candidates:
        if candidate.exists():
            return candidate

    return candidates[0]


def normalized_web_path(web_path):
    value = str(web_path or "").strip().replace("\\", "/")
    if not value:
        return ""

    parsed = urlparse(value)
    if parsed.scheme or parsed.netloc:
        value = parsed.path

    return "/" + value.lstrip("/")


def marker_matches_image(marker, image_path):
    marker_path = normalized_web_path(marker.get("imagePath"))
    target_path = normalized_web_path(image_path)
    return bool(marker_path and target_path and marker_path == target_path)


def marker_area_id(marker):
    value = marker.get("ihmAreaId")
    if value is None:
        return None

    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def find_template_position(ga_image_path, area_image_path):
    ga = cv2.imread(str(ga_image_path), cv2.IMREAD_GRAYSCALE)
    area = cv2.imread(str(area_image_path), cv2.IMREAD_GRAYSCALE)

    if ga is None or area is None:
        return None

    ga_h, ga_w = ga.shape[:2]
    area_h, area_w = area.shape[:2]

    if area_w > ga_w or area_h > ga_h:
        scale = min(ga_w / area_w, ga_h / area_h) * 0.8
        new_w = int(area_w * scale)
        new_h = int(area_h * scale)
        area = cv2.resize(area, (new_w, new_h), interpolation=cv2.INTER_AREA)
        area_h, area_w = area.shape[:2]

    result = cv2.matchTemplate(ga, area, cv2.TM_CCOEFF_NORMED)
    _, max_val, _, max_loc = cv2.minMaxLoc(result)

    x, y = max_loc

    return {
        "x": x,
        "y": y,
        "width": area_w,
        "height": area_h,
        "confidence": float(max_val)
    }


def text_size(draw, text, font):
    box = draw.textbbox((0, 0), text, font=font)
    return box[2] - box[0], box[3] - box[1]


def group_markers(marker_points, tolerance=6):
    groups = []

    for item in marker_points:
        added = False

        for group in groups:
            gx = group["x"]
            gy = group["y"]

            if abs(item["x"] - gx) <= tolerance and abs(item["y"] - gy) <= tolerance:
                group["labels"].append(item["label"])
                added = True
                break

        if not added:
            groups.append({
                "x": item["x"],
                "y": item["y"],
                "labels": [item["label"]]
            })

    return groups


def draw_callout_markers(draw, marker_points, layout_box, canvas_size):
    if not marker_points:
        return

    font = ImageFont.load_default()
    blue = (0, 0, 255)
    white = (255, 255, 255)

    canvas_w, canvas_h = canvas_size
    layout_x, layout_y, layout_w, layout_h = layout_box
    layout_right = layout_x + layout_w
    layout_bottom = layout_y + layout_h

    groups = group_markers(marker_points, tolerance=8)

    label_h = 18
    gap = 7
    margin = 8
    line_length = 70

    side_groups = {
        "top": [],
        "bottom": [],
        "left": [],
        "right": []
    }

    for group in groups:
        dist_top = group["y"] - layout_y
        dist_bottom = layout_bottom - group["y"]
        dist_left = group["x"] - layout_x
        dist_right = layout_right - group["x"]

        nearest = min(dist_top, dist_bottom, dist_left, dist_right)

        if nearest == dist_top:
            side = "top"
        elif nearest == dist_bottom:
            side = "bottom"
        elif nearest == dist_left:
            side = "left"
        else:
            side = "right"

        group["side"] = side
        side_groups[side].append(group)

    side_groups["top"].sort(key=lambda g: g["x"])
    side_groups["bottom"].sort(key=lambda g: g["x"])
    side_groups["left"].sort(key=lambda g: g["y"])
    side_groups["right"].sort(key=lambda g: g["y"])

    occupied = {
        "top": [],
        "bottom": [],
        "left": [],
        "right": []
    }

    for side, items in side_groups.items():
        for group in items:
            label = ",".join(group["labels"])
            text_w, _ = text_size(draw, label, font)
            label_w = max(44, text_w + 10)

            if side == "top":
                box_x = group["x"] - label_w / 2
                box_y = layout_y - line_length
                box_x = max(margin, min(box_x, canvas_w - label_w - margin))

                for old_x1, old_y1, old_x2, old_y2 in occupied[side]:
                    if not (box_x + label_w < old_x1 or box_x > old_x2):
                        box_y = min(box_y, old_y1 - label_h - gap)

                box_y = max(margin, box_y)
                line_end = (box_x + label_w / 2, box_y + label_h)

            elif side == "bottom":
                box_x = group["x"] - label_w / 2
                box_y = layout_bottom + line_length - label_h
                box_x = max(margin, min(box_x, canvas_w - label_w - margin))

                for old_x1, old_y1, old_x2, old_y2 in occupied[side]:
                    if not (box_x + label_w < old_x1 or box_x > old_x2):
                        box_y = max(box_y, old_y2 + gap)

                box_y = min(box_y, canvas_h - label_h - margin)
                line_end = (box_x + label_w / 2, box_y)

            elif side == "left":
                box_x = layout_x - line_length - label_w
                box_y = group["y"] - label_h / 2
                box_y = max(margin, min(box_y, canvas_h - label_h - margin))

                for old_x1, old_y1, old_x2, old_y2 in occupied[side]:
                    if not (box_y + label_h < old_y1 or box_y > old_y2):
                        box_y = old_y2 + gap

                box_y = min(box_y, canvas_h - label_h - margin)
                box_x = max(margin, box_x)
                line_end = (box_x + label_w, box_y + label_h / 2)

            else:
                box_x = layout_right + line_length
                box_y = group["y"] - label_h / 2
                box_y = max(margin, min(box_y, canvas_h - label_h - margin))

                for old_x1, old_y1, old_x2, old_y2 in occupied[side]:
                    if not (box_y + label_h < old_y1 or box_y > old_y2):
                        box_y = old_y2 + gap

                box_y = min(box_y, canvas_h - label_h - margin)
                box_x = min(box_x, canvas_w - label_w - margin)
                line_end = (box_x, box_y + label_h / 2)

            x1 = box_x
            y1 = box_y
            x2 = box_x + label_w
            y2 = box_y + label_h

            occupied[side].append((x1, y1, x2, y2))

            draw.rectangle([x1, y1, x2, y2], outline=blue, fill=white, width=1)
            draw.text((x1 + 5, y1 + 3), label, fill=blue, font=font)

            draw.line([group["x"], group["y"], line_end[0], line_end[1]], fill=blue, width=1)

            arrow = 4
            draw.line([group["x"], group["y"], group["x"] + arrow, group["y"] - arrow], fill=blue, width=1)
            draw.line([group["x"], group["y"], group["x"] + arrow, group["y"] + arrow], fill=blue, width=1)


def marker_points_for_image(markers, image_width, image_height, outer_pad):
    points = []

    for marker in markers:
        label = str(marker.get("markerLabel") or "").strip()
        if not label:
            continue

        points.append({
            "x": outer_pad + (float(marker.get("xPercent") or 0) / 100) * image_width,
            "y": outer_pad + (float(marker.get("yPercent") or 0) / 100) * image_height,
            "label": label
        })

    return points


def save_marked_layout(source_path, markers, output_path, outer_pad=120):
    base_image = Image.open(source_path).convert("RGB")
    image_width, image_height = base_image.size

    marked_image = Image.new(
        "RGB",
        (image_width + outer_pad * 2, image_height + outer_pad * 2),
        (255, 255, 255)
    )
    marked_image.paste(base_image, (outer_pad, outer_pad))

    draw = ImageDraw.Draw(marked_image)
    marker_points = marker_points_for_image(
        markers,
        image_width,
        image_height,
        outer_pad
    )
    draw_callout_markers(
        draw,
        marker_points,
        layout_box=(outer_pad, outer_pad, image_width, image_height),
        canvas_size=marked_image.size
    )

    output_path.parent.mkdir(parents=True, exist_ok=True)
    marked_image.save(output_path)


def main():
    if len(sys.argv) < 4:
        print("Usage: generate_ga_marker_map.py input.json output.png placements.json", file=sys.stderr)
        sys.exit(1)

    input_json = Path(sys.argv[1])
    output_image = Path(sys.argv[2])
    placement_json = Path(sys.argv[3])

    data = json.loads(input_json.read_text(encoding="utf-8"))

    ga_path = resolve_web_path(
        data.get("gaLayoutPhysicalPath") or data.get("gaLayout")
    )
    if not ga_path or not ga_path.exists():
        print(
            f"GA layout image not found: {data.get('gaLayout')} "
            f"(resolved: {ga_path})",
            file=sys.stderr
        )
        sys.exit(1)

    base_img = Image.open(ga_path).convert("RGB")
    ga_w, ga_h = base_img.size

    outer_pad = 120

    ga_img = Image.new(
        "RGB",
        (ga_w + outer_pad * 2, ga_h + outer_pad * 2),
        (255, 255, 255)
    )
    ga_img.paste(base_img, (outer_pad, outer_pad))

    draw = ImageDraw.Draw(ga_img)
    canvas_w, canvas_h = ga_img.size

    area_layouts = data.get("areaLayouts") or []
    markers = data.get("markers") or []
    existing_placements = data.get("placements") or []
    layout_count_by_area = {}
    for layout in area_layouts:
        area_id = layout.get("ihmAreaId")
        if area_id is not None:
            area_id = int(area_id)
            layout_count_by_area[area_id] = (
                layout_count_by_area.get(area_id, 0) + 1
            )
    areas_with_layout = set(layout_count_by_area)

    placement_by_area = {
        int(x["ihmAreaId"]): x
        for x in existing_placements
        if x.get("ihmAreaId") is not None
    }

    markers_by_area = {}
    for marker in markers:
        area_id = marker.get("ihmAreaId")
        if area_id is None:
            continue
        markers_by_area.setdefault(int(area_id), []).append(marker)

    output_placements = []
    common_marker_points = marker_points_for_image(
        [
            marker
            for marker in markers
            if marker_matches_image(marker, data.get("gaLayout"))
            or (
                not normalized_web_path(marker.get("imagePath"))
                and marker_area_id(marker) not in areas_with_layout
            )
        ],
        ga_w,
        ga_h,
        outer_pad
    )
    generated_layouts = [{
        "kind": "common",
        "title": "COMMON LAYOUT - MARKED CHECKPOINTS",
        "fileName": output_image.name
    }]

    for old_area_image in output_image.parent.glob("appendix4-area-*.png"):
        try:
            old_area_image.unlink(missing_ok=True)
        except OSError:
            pass

    for layout in area_layouts:
        area_id = layout.get("ihmAreaId")
        if area_id is None:
            continue

        area_id = int(area_id)
        area_path = resolve_web_path(layout.get("filePath"))

        if not area_path or not area_path.exists():
            continue

        area_markers = markers_by_area.get(area_id, [])
        image_markers = [
            marker
            for marker in area_markers
            if marker_matches_image(marker, layout.get("filePath"))
        ]

        if not image_markers and layout_count_by_area.get(area_id) == 1:
            image_markers = [
                marker
                for marker in area_markers
                if not normalized_web_path(marker.get("imagePath"))
            ]

        layout_id = layout.get("id") or area_id
        area_output = output_image.parent / f"appendix4-area-{area_id}-{layout_id}.png"
        save_marked_layout(area_path, image_markers, area_output, outer_pad)
        generated_layouts.append({
            "kind": "area",
            "areaId": area_id,
            "title": str(
                layout.get("areaName")
                or layout.get("originalFileName")
                or f"AREA {area_id}"
            ).strip() + " - MARKED CHECKPOINTS",
            "fileName": area_output.name
        })

        placement = placement_by_area.get(area_id)

        if placement and placement.get("isManuallyAdjusted"):
            x = outer_pad + float(placement["xPercent"]) / 100 * ga_w
            y = outer_pad + float(placement["yPercent"]) / 100 * ga_h
            w = float(placement["widthPercent"]) / 100 * ga_w
            h = float(placement["heightPercent"]) / 100 * ga_h
            confidence = float(placement.get("matchConfidence") or 1)
            is_auto = False
        else:
            match = find_template_position(ga_path, area_path)

            if (
                not match
                or float(match.get("confidence") or 0) < MIN_AUTO_MATCH_CONFIDENCE
            ):
                continue

            x = outer_pad + match["x"]
            y = outer_pad + match["y"]
            w = match["width"]
            h = match["height"]
            confidence = match["confidence"]
            is_auto = True

        x_percent = (x - outer_pad) / ga_w * 100
        y_percent = (y - outer_pad) / ga_h * 100
        width_percent = w / ga_w * 100
        height_percent = h / ga_h * 100

        output_placements.append({
            "ihmAreaId": area_id,
            "xPercent": round(x_percent, 4),
            "yPercent": round(y_percent, 4),
            "widthPercent": round(width_percent, 4),
            "heightPercent": round(height_percent, 4),
            "matchConfidence": round(confidence, 4),
            "isAutoMatched": is_auto
        })

        marker_points = []

        for marker in image_markers:
            marker_x = x + (float(marker.get("xPercent") or 0) / 100) * w
            marker_y = y + (float(marker.get("yPercent") or 0) / 100) * h

            label = str(marker.get("markerLabel") or "").strip()
            if not label:
                continue

            marker_points.append({
                "x": marker_x,
                "y": marker_y,
                "label": label
            })

        common_marker_points.extend(marker_points)

    draw_callout_markers(
        draw,
        common_marker_points,
        layout_box=(outer_pad, outer_pad, ga_w, ga_h),
        canvas_size=(canvas_w, canvas_h)
    )

    output_image.parent.mkdir(parents=True, exist_ok=True)
    ga_img.save(output_image)

    manifest_path = output_image.parent / "appendix4-layout-pages.json"
    manifest_path.write_text(
        json.dumps(generated_layouts, indent=2),
        encoding="utf-8"
    )

    placement_json.write_text(
        json.dumps(output_placements, indent=2),
        encoding="utf-8"
    )


if __name__ == "__main__":
    main()
