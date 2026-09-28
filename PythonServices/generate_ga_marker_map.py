import json
import sys
from pathlib import Path
from urllib.parse import urlparse

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

Image.MAX_IMAGE_PIXELS = None
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

    match_max = 1800
    match_scale = min(1.0, match_max / max(ga_w, ga_h), match_max / max(area_w, area_h))
    if match_scale < 1.0:
        ga = cv2.resize(ga, (max(1, int(ga_w * match_scale)), max(1, int(ga_h * match_scale))), interpolation=cv2.INTER_AREA)
        area = cv2.resize(area, (max(1, int(area_w * match_scale)), max(1, int(area_h * match_scale))), interpolation=cv2.INTER_AREA)
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

def build_content_integral(image):
    gray = cv2.cvtColor(np.array(image.convert("RGB")), cv2.COLOR_RGB2GRAY)
    mask = (gray < 235).astype(np.uint8)
    kernel = np.ones((3, 3), np.uint8)
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel)
    return cv2.integral(mask)

def rect_content_ratio(rect, integral, image_w, image_h):
    x1, y1, x2, y2 = [int(round(v)) for v in rect]
    x1, y1 = max(0, x1), max(0, y1)
    x2, y2 = min(image_w, x2), min(image_h, y2)
    if x2 <= x1 or y2 <= y1:
        return 1.0
    occupied = integral[y2, x2] - integral[y1, x2] - integral[y2, x1] + integral[y1, x1]
    return float(occupied) / float((x2 - x1) * (y2 - y1))

def find_local_drawing_bounds(content_integral, point, layout_box):
    lx, ly, lw, lh = layout_box
    px, py = point
    # Use a bounded local window around the marker; this prevents the complete
    # page border from becoming the normal routing destination.
    half_w = max(180, min(lw * 0.22, 900))
    half_h = max(140, min(lh * 0.22, 700))
    left = max(lx, px - half_w)
    top = max(ly, py - half_h)
    right = min(lx + lw, px + half_w)
    bottom = min(ly + lh, py + half_h)
    return left, top, right, bottom

def detect_compartment_rects(image, outer_pad=0):
    cv_img = cv2.cvtColor(np.array(image.convert("RGB")), cv2.COLOR_RGB2BGR)
    hsv = cv2.cvtColor(cv_img, cv2.COLOR_BGR2HSV)

    # Detect colored compartment blocks. This ignores white background,
    # pale vessel fill, black outlines, grid lines, and blue leader text.
    saturation = hsv[:, :, 1]
    value = hsv[:, :, 2]
    mask = cv2.inRange(saturation, 35, 255)
    mask = cv2.bitwise_and(mask, cv2.inRange(value, 80, 255))

    kernel = np.ones((5, 5), np.uint8)
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel, iterations=2)

    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

    rects = []
    image_w, image_h = image.size

    for contour in contours:
        x, y, w, h = cv2.boundingRect(contour)

        if w < 35 or h < 22:
            continue

        if w > image_w * 0.75 or h > image_h * 0.75:
            continue

        rects.append((
            outer_pad + x,
            outer_pad + y,
            outer_pad + x + w,
            outer_pad + y + h,
        ))

    return rects

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


def draw_callout_markers(draw, marker_points, layout_box, canvas_size, obstacle_rects=None, content_integral=None, labels_outside=False):
    if not marker_points:
        return

    try:
        font = ImageFont.truetype("arialbd.ttf", 42)
    except OSError:
        font = ImageFont.load_default()

    blue = (0, 0, 255)
    pink = (255, 0, 128)
    white = (255, 255, 255)

    def marker_color(label):
        # Visual-check markers are pink; sampling markers remain blue.
        # Use the generated label prefix so this also works after Excel
        # replacement changes S01 to V01 without changing coordinates.
        return pink if str(label or "").strip().upper().startswith("V") else blue

    canvas_w, canvas_h = canvas_size
    layout_x, layout_y, layout_w, layout_h = layout_box
    layout_right = layout_x + layout_w
    layout_bottom = layout_y + layout_h

    groups = group_markers(marker_points, tolerance=3)

    label_h = 54
    gap = 14
    margin = 10
    # Appendix 4 callout cells start slightly below the top edge. Keep a small
    # readable gap between every stacked cell on both left and right columns.
    first_label_offset = 40 if labels_outside else 0
    outside_gap = 16
    lane_gap = label_h + 16 if labels_outside else label_h + gap
    route_gap = 10
    routing_lane_gap = 28
    base_lane_offset = 30
    collision_pad = 8
    preferred_length = max(45, min(min(layout_w, layout_h) * 0.07, 140))

    placements = []
    used_boxes = []
    existing_segments = []
    reserved_bend_points = set()
    target_boxes = []
    obstacle_rects = obstacle_rects or []

    for group in groups:
        target_boxes.append((
            group.get("draw_x", group["x"]) - 9,
            group.get("draw_y", group["y"]) - 9,
            group.get("draw_x", group["x"]) + 9,
            group.get("draw_y", group["y"]) + 9,
        ))

    def boxes_overlap(a, b, pad=gap):
        ax1, ay1, ax2, ay2 = a
        bx1, by1, bx2, by2 = b
        return not (
            ax2 + pad < bx1 or
            ax1 - pad > bx2 or
            ay2 + pad < by1 or
            ay1 - pad > by2
        )

    def has_overlap(box):
        return any(boxes_overlap(box, old_box) for old_box in used_boxes)

    def clean_box(box):
        if content_integral is None:
            return True
        padded = (box[0] - 8, box[1] - 8, box[2] + 8, box[3] + 8)
        return rect_content_ratio(padded, content_integral, canvas_w, canvas_h) <= 0.04

    def segment_box(segment):
        (x1, y1), (x2, y2) = segment
        return (
            min(x1, x2) - collision_pad,
            min(y1, y2) - collision_pad,
            max(x1, x2) + collision_pad,
            max(y1, y2) + collision_pad,
        )

    def segment_hits_rect(segment, rect, allow_target=False):
        if allow_target:
            return False

        (x1, y1), (x2, y2) = segment
        rx1, ry1, rx2, ry2 = rect
        rx1 -= collision_pad
        ry1 -= collision_pad
        rx2 += collision_pad
        ry2 += collision_pad

        if abs(x1 - x2) < 0.1:
            return rx1 <= x1 <= rx2 and not (max(y1, y2) < ry1 or min(y1, y2) > ry2)

        if abs(y1 - y2) < 0.1:
            return ry1 <= y1 <= ry2 and not (max(x1, x2) < rx1 or min(x1, x2) > rx2)

        return boxes_overlap(segment_box(segment), (rx1, ry1, rx2, ry2), pad=0)

    def segments_touch_or_overlap(a, b, tolerance=10):
        ax1, ay1 = a[0]
        ax2, ay2 = a[1]
        bx1, by1 = b[0]
        bx2, by2 = b[1]

        a_vertical = abs(ax1 - ax2) < 0.1
        b_vertical = abs(bx1 - bx2) < 0.1
        a_horizontal = abs(ay1 - ay2) < 0.1
        b_horizontal = abs(by1 - by2) < 0.1

        if a_vertical and b_vertical and abs(ax1 - bx1) <= tolerance:
            return not (
                max(ay1, ay2) < min(by1, by2) - tolerance or
                min(ay1, ay2) > max(by1, by2) + tolerance
            )

        if a_horizontal and b_horizontal and abs(ay1 - by1) <= tolerance:
            return not (
                max(ax1, ax2) < min(bx1, bx2) - tolerance or
                min(ax1, ax2) > max(bx1, bx2) + tolerance
            )

        return False


    def segments_cross(a, b):
        ax1, ay1 = a[0]
        ax2, ay2 = a[1]
        bx1, by1 = b[0]
        bx2, by2 = b[1]
        if any((p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2 <= collision_pad ** 2 for p in a for q in b):
            return True

        a_vertical = abs(ax1 - ax2) < 0.1
        b_vertical = abs(bx1 - bx2) < 0.1
        a_horizontal = abs(ay1 - ay2) < 0.1
        b_horizontal = abs(by1 - by2) < 0.1

        if a_vertical and b_horizontal:
            return min(bx1, bx2) <= ax1 <= max(bx1, bx2) and min(ay1, ay2) <= by1 <= max(ay1, ay2)

        if a_horizontal and b_vertical:
            return min(ax1, ax2) <= bx1 <= max(ax1, ax2) and min(by1, by2) <= ay1 <= max(by1, by2)

        if a_vertical and b_vertical and abs(ax1 - bx1) < collision_pad:
            return not (max(ay1, ay2) < min(by1, by2) or min(ay1, ay2) > max(by1, by2))

        if a_horizontal and b_horizontal and abs(ay1 - by1) < collision_pad:
            return not (max(ax1, ax2) < min(bx1, bx2) or min(ax1, ax2) > max(bx1, bx2))

        return False

    def path_segments(path):
        return list(zip(path, path[1:]))

    def rect_contains_point(rect, point):
        x1, y1, x2, y2 = rect
        x, y = point
        return x1 <= x <= x2 and y1 <= y <= y2

    def bend_key(point):
        return (round(point[0] / 4), round(point[1] / 4))

    def marker_side(group):
        local_left, local_top, local_right, local_bottom = group.get("local_bounds", (layout_x, layout_y, layout_right, layout_bottom))
        dist_top = group["y"] - local_top
        dist_bottom = local_bottom - group["y"]
        dist_left = group["x"] - local_left
        dist_right = local_right - group["x"]

        distances = [
            ("top", dist_top),
            ("bottom", dist_bottom),
            ("left", dist_left),
            ("right", dist_right),
        ]
        return min(distances, key=lambda item: item[1])[0]

    def label_box_for_side(group, label_w, side, lane):
        target_x = group.get("draw_x", group["x"])
        target_y = group.get("draw_y", group["y"])
        local_left, local_top, local_right, local_bottom = group.get("local_bounds", (layout_x, layout_y, layout_right, layout_bottom))
        if side == "top":
            box_x = target_x - label_w / 2
            box_y = (layout_y - outside_gap - label_h if labels_outside else target_y - preferred_length - label_h) - (lane * lane_gap)
        elif side == "bottom":
            box_x = target_x - label_w / 2
            box_y = (layout_bottom + outside_gap if labels_outside else target_y + preferred_length) + (lane * lane_gap)
        elif side == "left":
            box_x = (layout_x - outside_gap - label_w if labels_outside else target_x - preferred_length - label_w) - (lane * (label_w + gap))
            box_y = target_y - label_h / 2
        else:
            box_x = (layout_right + outside_gap if labels_outside else target_x + preferred_length) + (lane * (label_w + gap))
            box_y = target_y - label_h / 2

        box_x = max(margin, min(box_x, canvas_w - label_w - margin))
        box_y = max(margin, min(box_y, canvas_h - label_h - margin))
        box = (box_x, box_y, box_x + label_w, box_y + label_h)
        return box

    def label_anchor(box, side):
        x1, y1, x2, y2 = box
        if side == "top":
            return ((x1 + x2) / 2, y2)
        if side == "bottom":
            return ((x1 + x2) / 2, y1)
        if side == "left":
            return (x2, (y1 + y2) / 2)
        return (x1, (y1 + y2) / 2)

    def target_point(group, side):
        x = group.get("draw_x", group["x"])
        y = group.get("draw_y", group["y"])
        if side == "top":
            return (x, max(layout_y, y - 1))
        if side == "bottom":
            return (x, min(layout_bottom, y + 1))
        if side == "left":
            return (max(layout_x, x - 1), y)
        return (min(layout_right, x + 1), y)

    def path_score(path, target_rect):
        hard_collisions = 0
        soft_score = 0
        segments = path_segments(path)
        target = path[-1]

        for segment in segments:
            soft_score += abs(segment[0][0] - segment[1][0]) + abs(segment[0][1] - segment[1][1])

            for box in used_boxes:
                if segment_hits_rect(segment, box):
                    hard_collisions += 1

            for other_target in target_boxes:
                if other_target != target_rect and segment_hits_rect(segment, other_target):
                    hard_collisions += 1

            for obstacle_rect in obstacle_rects:
                # The selected marker may be inside its own compartment. Allow
                # the final connection to enter only that target compartment.
                if rect_contains_point(obstacle_rect, target):
                    continue
                if segment_hits_rect(segment, obstacle_rect):
                    hard_collisions += 1

            for existing in existing_segments:
                if segments_touch_or_overlap(segment, existing):
                    hard_collisions += 1
                elif segments_cross(segment, existing):
                    hard_collisions += 1

        bends = max(0, len(path) - 2)
        for point in path[1:-1]:
            if bend_key(point) in reserved_bend_points:
                hard_collisions += 1

        soft_score += bends * 300

        return hard_collisions, soft_score

    def choose_route(candidates, target_rect):
        scored = [
            (path_score(candidate, target_rect), candidate)
            for candidate in candidates
            if len(candidate) >= 2
        ]
        clear = [item for item in scored if item[0][0] == 0]

        if clear:
            return min(clear, key=lambda item: item[0][1])[1]

        return min(scored, key=lambda item: (item[0][0], item[0][1]))[1]

    def route_path(anchor, target, side, target_rect, route_index=0):
        def path_has_collision(path):
            for segment in path_segments(path):
                for box in used_boxes:
                    if segment_hits_rect(segment, box):
                        return True

                for other_target in target_boxes:
                    if other_target != target_rect and segment_hits_rect(segment, other_target):
                        return True

                for obstacle_rect in obstacle_rects:
                    # Allow only the final target point to enter its own cell.
                    if rect_contains_point(obstacle_rect, target):
                        continue

                    if segment_hits_rect(segment, obstacle_rect):
                        return True

                for existing in existing_segments:
                    if segments_touch_or_overlap(segment, existing, tolerance=14):
                        return True
                    if segments_cross(segment, existing):
                        return True

            for bend in path[1:-1]:
                if bend_key(bend) in reserved_bend_points:
                    return True

            return False

        def path_length(path):
            total = 0
            for p1, p2 in path_segments(path):
                total += abs(p1[0] - p2[0]) + abs(p1[1] - p2[1])
            return total + (max(0, len(path) - 2) * 80)

        candidates_by_priority = []

        # Give every left/right callout its own vertical routing lane. Without
        # this, labels with different Y positions can collapse onto one shared
        # vertical segment near the margin.
        if side in ("left", "right"):
            # Keep the lane on the drawing-facing side of the label column.
            # Every callout gets a different X lane, so no common trunk can form.
            # Short horizontal exit from the cell, followed by an independent
            # angled leader to the exact target. No shared trunk is used.
            direction = 1 if side == "left" else -1
            short_exit_x = anchor[0] + direction * (base_lane_offset + route_index * routing_lane_gap)
            return [anchor, (short_exit_x, anchor[1]), target]

        # Priority 1: direct straight line only if truly aligned.
        direct_candidates = []
        if side in ("top", "bottom") and abs(anchor[0] - target[0]) < 1:
            direct_candidates.append([anchor, target])
        elif side in ("left", "right") and abs(anchor[1] - target[1]) < 1:
            direct_candidates.append([anchor, target])

        candidates_by_priority.append(direct_candidates)

        # Priority 2: one-bend L route.
        if side in ("top", "bottom"):
            candidates_by_priority.append([
                [anchor, (anchor[0], target[1]), target],
                [anchor, (target[0], anchor[1]), target],
            ])
        else:
            candidates_by_priority.append([
                [anchor, (target[0], anchor[1]), target],
                [anchor, (anchor[0], target[1]), target],
            ])

        # Priority 3 and 4: route through blank lanes using horizontal offset.
        if side in ("top", "bottom"):
            outside_step = -route_gap if side == "top" else route_gap
            outside_y = layout_y - route_gap if side == "top" else layout_bottom + route_gap

            lane_candidates = []
            for offset in (12, -12, 22, -22, 34, -34, 46, -46):
                lane_x = target[0] + offset
                lane_x = max(layout_x - outside_gap, min(lane_x, layout_right + outside_gap))

                # 7-shape: small vertical from label, horizontal to blank lane,
                # then vertical to target. This separates close vertical lines.
                lane_candidates.append([
                    anchor,
                    (anchor[0], anchor[1] + outside_step),
                    (lane_x, anchor[1] + outside_step),
                    (lane_x, target[1]),
                    target,
                ])

                # Wider outside lane fallback.
                lane_candidates.append([
                    anchor,
                    (anchor[0], outside_y),
                    (lane_x, outside_y),
                    (lane_x, target[1]),
                    target,
                ])

            candidates_by_priority.append(lane_candidates)

        else:
            outside_step = -route_gap if side == "left" else route_gap
            outside_x = layout_x - route_gap if side == "left" else layout_right + route_gap

            lane_candidates = []
            for offset in (12, -12, 22, -22, 34, -34, 46, -46):
                lane_y = target[1] + offset
                lane_y = max(layout_y - outside_gap, min(lane_y, layout_bottom + outside_gap))

                lane_candidates.append([
                    anchor,
                    (anchor[0] + outside_step, anchor[1]),
                    (anchor[0] + outside_step, lane_y),
                    (target[0], lane_y),
                    target,
                ])

                lane_candidates.append([
                    anchor,
                    (outside_x, anchor[1]),
                    (outside_x, lane_y),
                    (target[0], lane_y),
                    target,
                ])

            candidates_by_priority.append(lane_candidates)

        # Accept first priority group that has zero collision.
        for candidates in candidates_by_priority:
            safe_candidates = [
                path for path in candidates
                if not path_has_collision(path)
            ]

            if safe_candidates:
                return min(safe_candidates, key=path_length)

        # Last fallback: choose shortest offset route, not straight.
        fallback_candidates = candidates_by_priority[-1]
        return min(fallback_candidates, key=path_length)

    def draw_arrowhead(path, color):
        if len(path) < 2:
            return

        tip = path[-1]
        prev = path[-2]
        dx = tip[0] - prev[0]
        dy = tip[1] - prev[1]

        size = 8
        if abs(dx) > abs(dy):
            direction = 1 if dx > 0 else -1
            arrow = [
                tip,
                (tip[0] - direction * size, tip[1] - 5),
                (tip[0] - direction * size, tip[1] + 5),
            ]
        else:
            direction = 1 if dy > 0 else -1
            arrow = [
                tip,
                (tip[0] - 5, tip[1] - direction * size),
                (tip[0] + 5, tip[1] - direction * size),
            ]

        draw.polygon(arrow, fill=color)

    side_groups = {"top": [], "bottom": [], "left": [], "right": []}
    for group in groups:
        side_groups[marker_side(group)].append(group)

    side_groups["top"].sort(key=lambda g: g["x"])
    side_groups["bottom"].sort(key=lambda g: g["x"])
    side_groups["left"].sort(key=lambda g: g["y"])
    side_groups["right"].sort(key=lambda g: g["y"])

    ordered_groups = (
        side_groups["top"] +
        side_groups["bottom"] +
        side_groups["left"] +
        side_groups["right"]
    )

    # Deterministic Appendix 4 arrangement: fill a vertical left margin column,
    # then continue in a vertical right margin column. This deliberately avoids
    # nearest-side placement and keeps every label outside the drawing.
    column_capacity = max(
        1,
        int((canvas_h - margin - first_label_offset) // lane_gap)
    )

    for group_index, group in enumerate(ordered_groups):
        group["local_bounds"] = find_local_drawing_bounds(
            content_integral,
            (group["x"], group["y"]),
            (layout_x, layout_y, layout_w, layout_h)
        ) if content_integral is not None else (layout_x, layout_y, layout_right, layout_bottom)
        label = ", ".join(group["labels"])
        text_w, text_h = text_size(draw, label, font)
        label_w = max(110, text_w + 28)
        side = "left" if group_index < column_capacity else "right"
        lane = group_index if side == "left" else group_index - column_capacity
        box_x = (layout_x - outside_gap - label_w) if side == "left" else (layout_right + outside_gap)
        box_y = margin + first_label_offset + lane * lane_gap
        chosen_box = (box_x, box_y, box_x + label_w, box_y + label_h)

        target_rect = (
            group.get("draw_x", group["x"]) - 9,
            group.get("draw_y", group["y"]) - 9,
            group.get("draw_x", group["x"]) + 9,
            group.get("draw_y", group["y"]) + 9,
        )
        anchor = label_anchor(chosen_box, side)
        # Terminate at the stored marker coordinate itself. Only label placement
        # and leader routing are changed.
        target = (
            group.get("draw_x", group["x"]),
            group.get("draw_y", group["y"])
        )
        chosen_path = route_path(anchor, target, side, target_rect, group_index)

        used_boxes.append(chosen_box)
        existing_segments.extend(path_segments(chosen_path))
        for bend in chosen_path[1:-1]:
            reserved_bend_points.add(bend_key(bend))
        placements.append({
            "group": group,
            "label": label,
            "text_h": text_h,
            "text_w": text_w,
            "label_w": label_w,
            "box": chosen_box,
            "path": chosen_path,
        })

    for item in placements:
        path = item["path"]
        color = marker_color(item["label"])

        draw.line(
            [coord for point in path for coord in point],
            fill=color,
            width=2,
        )
        draw_arrowhead(path, color)

        group = item["group"]
        dot_radius = 4
        draw.ellipse(
            [
                group.get("draw_x", group["x"]) - dot_radius,
                group.get("draw_y", group["y"]) - dot_radius,
                group.get("draw_x", group["x"]) + dot_radius,
                group.get("draw_y", group["y"]) + dot_radius,
            ],
            outline=color,
            fill=white,
            width=2,
        )

    for item in placements:
        x1, y1, x2, y2 = item["box"]
        label = item["label"]
        text_h = item["text_h"]

        color = marker_color(label)
        draw.rectangle([x1, y1, x2, y2], outline=color, fill=white, width=4)
        text_x = x1 + max(0, (label_w - text_w) / 2)
        text_y = y1 + max(6, (label_h - text_h) / 2 - 2)

        draw.text(
            (text_x, text_y),
            label,
            fill=color,
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

    return (
        "ocr-existing-label" in notes
        or "manual-for-existing-image" in notes
        or source == "existing-image"
    )

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

    max_side = 7000
    if max(image_width, image_height) > max_side:
        scale = max_side / max(image_width, image_height)
        new_size = (max(1, int(image_width * scale)), max(1, int(image_height * scale)))
        base_image = base_image.resize(new_size, Image.Resampling.LANCZOS)
        image_width, image_height = base_image.size

    min_width = 2400
    if image_width < min_width:
        scale = min_width / image_width
        image_width = int(image_width * scale)
        image_height = int(image_height * scale)
        base_image = base_image.resize((image_width, image_height), Image.Resampling.LANCZOS)

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
    obstacle_rects = detect_compartment_rects(base_image, outer_pad)
    draw_callout_markers(
        draw,
        marker_points,
        layout_box=(outer_pad, outer_pad, image_width, image_height),
        canvas_size=marked_image.size,
        obstacle_rects=obstacle_rects,
        content_integral=build_content_integral(marked_image),
        # Appendix 4 labels must stay in the outer white margin.
        labels_outside=True
    )

    output_path.parent.mkdir(parents=True, exist_ok=True)
    marked_image.save(output_path, dpi=(300, 300), quality=95)


def main():
    if len(sys.argv) < 4:
        print("Usage: generate_ga_marker_map.py input.json output.png placements.json", file=sys.stderr)
        sys.exit(1)

    input_json = Path(sys.argv[1])
    output_image = Path(sys.argv[2])
    placement_json = Path(sys.argv[3])

    data = json.loads(input_json.read_text(encoding="utf-8"))
    print("APPENDIX4: input loaded", file=sys.stderr, flush=True)

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
    print(f"APPENDIX4: GA image loaded path={ga_path}", file=sys.stderr, flush=True)
    ga_w, ga_h = base_img.size

    max_side = 7000
    if max(ga_w, ga_h) > max_side:
        scale = max_side / max(ga_w, ga_h)
        new_size = (max(1, int(ga_w * scale)), max(1, int(ga_h * scale)))
        base_img = base_img.resize(new_size, Image.Resampling.LANCZOS)
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
    ga_layout_web_path = normalized_web_path(data.get("gaLayout"))
    machinery_layouts = data.get("machineryLayouts") or []
    if not machinery_layouts and data.get("machineryLayout"):
        machinery_layouts = [{"filePath": data.get("machineryLayout")}]
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
    common_obstacle_rects = detect_compartment_rects(base_img, outer_pad)
    generated_layouts = [{
        "kind": "common",
        "title": "COMMON LAYOUT - MARKED CHECKPOINTS",
        "fileName": output_image.name
    }]
    generated_source_paths = {normalized_web_path(data.get("gaLayout"))}

    # Remove outputs from older runs so stale machinery pages cannot survive
    # when the current upload has only one marked layout.
    for old_machinery_image in output_image.parent.glob("appendix4-machinery-*.png"):
        try:
            old_machinery_image.unlink(missing_ok=True)
        except OSError:
            pass

    for index, machinery in enumerate(machinery_layouts, start=1):
        machinery_layout_path = machinery.get("filePath") if isinstance(machinery, dict) else machinery
        machinery_web_path = normalized_web_path(machinery_layout_path)
        if not machinery_web_path or machinery_web_path in generated_source_paths:
            continue
        machinery_markers = [marker for marker in markers if machinery_web_path and marker_matches_image(marker, machinery_web_path)]
        machinery_path = resolve_web_path(machinery_layout_path)
        if not machinery_path or not machinery_path.exists() or not machinery_markers:
            continue
        print(f"APPENDIX4: generating machinery file={index} markers={len(machinery_markers)}", file=sys.stderr, flush=True)
        machinery_output = output_image.parent / f"appendix4-machinery-{index}-marked.png"
        save_marked_layout(machinery_path, machinery_markers, machinery_output, outer_pad)
        generated_layouts.append({
            "kind": "machinery",
            "title": "MACHINERY LAYOUT - MARKED CHECKPOINTS",
            "fileName": machinery_output.name
        })
        generated_source_paths.add(machinery_web_path)
        print(f"APPENDIX4: machinery image saved file={index}", file=sys.stderr, flush=True)

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
        area_file_path = layout.get("filePath")
        area_path = resolve_web_path(area_file_path)

        if not area_path or not area_path.exists():
            continue

        # If common layout is missing, backend uses the first area layout as the
        # Appendix 4 main map. Do not add that same area layout again as an extra page.
        if ga_layout_web_path and normalized_web_path(area_file_path) == ga_layout_web_path:
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

        area_web_path = normalized_web_path(area_file_path)
        if not image_markers or area_web_path in generated_source_paths:
            continue

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
        generated_source_paths.add(area_web_path)

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
        canvas_size=ga_img.size,
        obstacle_rects=common_obstacle_rects,
        content_integral=build_content_integral(ga_img),
        # Keep generated callouts outside the GA drawing, matching hazardous output.
        labels_outside=True
    )

    output_image.parent.mkdir(parents=True, exist_ok=True)
    ga_img.save(output_image, dpi=(300, 300), quality=95)
    print(f"APPENDIX4: GA image saved path={output_image}", file=sys.stderr, flush=True)

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
