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

def dedupe_markers_by_label_and_image(markers):
    latest = {}

    for marker in markers:
        label = str(marker.get("markerLabel") or "").strip()
        image_path = normalized_web_path(marker.get("imagePath"))

        if not label or not image_path:
            continue

        key = (label.upper(), image_path)
        marker_id = int(marker.get("id") or 0)

        existing = latest.get(key)
        existing_id = int(existing.get("id") or 0) if existing else -1

        if existing is None or marker_id >= existing_id:
            latest[key] = marker

    return list(latest.values())

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

    try:
        font = ImageFont.truetype("arial.ttf", 20)
    except OSError:
        font = ImageFont.load_default()

    blue = (0, 0, 255)
    white = (255, 255, 255)

    canvas_w, canvas_h = canvas_size
    layout_x, layout_y, layout_w, layout_h = layout_box
    layout_right = layout_x + layout_w
    layout_bottom = layout_y + layout_h

    groups = group_markers(marker_points, tolerance=28)

    label_h = 28
    gap = 12
    margin = 8
    line_gap = 22

    side_groups = {
        "top": [],
        "bottom": [],
        "left": [],
        "right": [],
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

    lanes = {
        "top": [],
        "bottom": [],
        "left": [],
        "right": [],
    }

    def reserve_horizontal_lane(side, x1, x2):
        for lane_index, lane_items in enumerate(lanes[side]):
            overlaps = any(
                not (x2 + gap < old_x1 or x1 - gap > old_x2)
                for old_x1, old_x2 in lane_items
            )
            if not overlaps:
                lane_items.append((x1, x2))
                return lane_index

        lanes[side].append([(x1, x2)])
        return len(lanes[side]) - 1

    def reserve_vertical_lane(side, y1, y2):
        for lane_index, lane_items in enumerate(lanes[side]):
            overlaps = any(
                not (y2 + gap < old_y1 or y1 - gap > old_y2)
                for old_y1, old_y2 in lane_items
            )
            if not overlaps:
                lane_items.append((y1, y2))
                return lane_index

        lanes[side].append([(y1, y2)])
        return len(lanes[side]) - 1

    placements = []
    used_boxes = []

    def boxes_overlap(a, b):
        ax1, ay1, ax2, ay2 = a
        bx1, by1, bx2, by2 = b
        return not (
            ax2 + gap < bx1 or
            ax1 - gap > bx2 or
            ay2 + gap < by1 or
            ay1 - gap > by2
        )

    def has_overlap(box):
        return any(boxes_overlap(box, old_box) for old_box in used_boxes)

    for side, items in side_groups.items():
        for group in items:
            label = ", ".join(group["labels"])
            text_w, text_h = text_size(draw, label, font)
            label_w = max(58, text_w + 16)

            if side == "top":
                box_x = group["x"] - label_w / 2
                box_x = max(margin, min(box_x, canvas_w - label_w - margin))
                lane = reserve_horizontal_lane(side, box_x, box_x + label_w)
                box_y = layout_y - line_gap - ((lane + 1) * (label_h + gap))
                box_y = max(margin, box_y)
                line_end = (box_x + label_w / 2, box_y + label_h)
                route = (group["x"], line_end[1] + (line_gap / 2))

            elif side == "bottom":
     

                box_x = group["x"] - label_w / 2
                box_x = max(margin, min(box_x, canvas_w - label_w - margin))
                lane = reserve_horizontal_lane(side, box_x, box_x + label_w)
                box_y = layout_bottom + line_gap + (lane * (label_h + gap))
                box_y = min(box_y, canvas_h - label_h - margin)
                line_end = (box_x + label_w / 2, box_y)
                route = (group["x"], line_end[1] - (line_gap / 2))

                
            elif side == "left":
                box_y = group["y"] - label_h / 2
                box_y = max(margin, min(box_y, canvas_h - label_h - margin))
                lane = reserve_vertical_lane(side, box_y, box_y + label_h)
                box_x = layout_x - line_gap - label_w - (lane * (label_w + gap))
                box_x = max(margin, box_x)
                line_end = (box_x + label_w, box_y + label_h / 2)
                route = (line_end[0] + (line_gap / 2), group["y"])

            else:
                box_y = group["y"] - label_h / 2
                box_y = max(margin, min(box_y, canvas_h - label_h - margin))
                lane = reserve_vertical_lane(side, box_y, box_y + label_h)
                box_x = layout_right + line_gap + (lane * (label_w + gap))
                box_x = min(box_x, canvas_w - label_w - margin)
                line_end = (box_x, box_y + label_h / 2)
                route = (line_end[0] - (line_gap / 2), group["y"])

            box = (box_x, box_y, box_x + label_w, box_y + label_h)

            if has_overlap(box):
                alternate_sides = ["top", "bottom", "left", "right"]

                for alt_side in alternate_sides:
                    if alt_side == side:
                        continue

                    if alt_side == "top":
                        test_x = group["x"] - label_w / 2
                        test_x = max(margin, min(test_x, canvas_w - label_w - margin))
                        test_y = layout_y - line_gap - label_h
                        test_y = max(margin, test_y)
                        test_line_end = (test_x + label_w / 2, test_y + label_h)
                        test_route = (group["x"], test_line_end[1] + (line_gap / 2))

                    elif alt_side == "bottom":
                        test_x = group["x"] - label_w / 2
                        test_x = max(margin, min(test_x, canvas_w - label_w - margin))
                        test_y = layout_bottom + line_gap
                        test_y = min(test_y, canvas_h - label_h - margin)
                        test_line_end = (test_x + label_w / 2, test_y)
                        test_route = (group["x"], test_line_end[1] - (line_gap / 2))

                    elif alt_side == "left":
                        test_y = group["y"] - label_h / 2
                        test_y = max(margin, min(test_y, canvas_h - label_h - margin))
                        test_x = layout_x - line_gap - label_w
                        test_x = max(margin, test_x)
                        test_line_end = (test_x + label_w, test_y + label_h / 2)
                        test_route = (test_line_end[0] + (line_gap / 2), group["y"])

                    else:
                        test_y = group["y"] - label_h / 2
                        test_y = max(margin, min(test_y, canvas_h - label_h - margin))
                        test_x = layout_right + line_gap
                        test_x = min(test_x, canvas_w - label_w - margin)
                        test_line_end = (test_x, test_y + label_h / 2)
                        test_route = (test_line_end[0] - (line_gap / 2), group["y"])

                    test_box = (test_x, test_y, test_x + label_w, test_y + label_h)

                    if not has_overlap(test_box):
                        box_x = test_x
                        box_y = test_y
                        line_end = test_line_end
                        route = test_route
                        box = test_box
                        break

            used_boxes.append(box)
            placements.append({
                "group": group,
                "label": label,
                "text_h": text_h,
                "box": box,
                "line_end": line_end,
                "route": route,
            })

    # Draw connectors first. Label rectangles are drawn after this, so lines
    # cannot visibly cut through label boxes.
    for item in placements:
        group = item["group"]
        route_x, route_y = item["route"]
        end_x, end_y = item["line_end"]

        draw.line(
            [group.get("draw_x", group["x"]), group.get("draw_y", group["y"]), route_x, route_y, end_x, end_y],
            fill=blue,
            width=2,
        )

        dot_radius = 4
        draw.ellipse(
            [
                group.get("draw_x", group["x"]) - dot_radius,
                group.get("draw_y", group["y"]) - dot_radius,
                group.get("draw_x", group["x"]) + dot_radius,
                group.get("draw_y", group["y"]) + dot_radius,
            ],
            outline=blue,
            fill=white,
            width=2,
        )

    # Draw labels last so no connector line cuts through a label/cell.
    for item in placements:
        x1, y1, x2, y2 = item["box"]
        label = item["label"]
        text_h = item["text_h"]

        draw.rectangle([x1, y1, x2, y2], outline=blue, fill=white, width=2)
        draw.text(
            (x1 + 8, y1 + max(2, (label_h - text_h) / 2 - 1)),
            label,
            fill=blue,
            font=font,
        )
def percent_to_unit(value):
    try:
        number = float(value or 0)
    except (TypeError, ValueError):
        return 0.0

    if number > 1:
        number = number / 100.0

    return max(0.0, min(number, 1.0))
def is_ocr_existing_label_marker(marker):
    notes = str(
        marker.get("notes")
        or marker.get("Notes")
        or ""
    ).strip().lower()

    source = str(
        marker.get("source")
        or marker.get("Source")
        or ""
    ).strip().lower()

    return "ocr-existing-label" in notes or source == "existing-image"

def marker_points_for_image(markers, image_width, image_height, outer_pad, y_offset=0):
    points = []

    for marker in markers:
        if marker.get("suppressRender") or marker.get("SuppressRender"):
            continue
        if is_ocr_existing_label_marker(marker):
            continue

        label = str(marker.get("markerLabel") or "").strip()
        if not label:
            continue

        x = outer_pad + percent_to_unit(marker.get("xPercent")) * image_width
        y = outer_pad + percent_to_unit(marker.get("yPercent")) * image_height + y_offset

        y = max(outer_pad, min(y, outer_pad + image_height))

        points.append({
            "x": x,
            "y": y,
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

    outer_pad = 180

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
    common_markers = dedupe_markers_by_label_and_image(
    [
        marker
        for marker in markers
        if marker_matches_image(marker, data.get("gaLayout"))
    ]
    )

    common_marker_points = marker_points_for_image(
        common_markers,
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
