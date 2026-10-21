import json
from itertools import pairwise
import time
from collections import Counter
import sys
from pathlib import Path
from urllib.parse import urlparse

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

Image.MAX_IMAGE_PIXELS = None
MIN_AUTO_MATCH_CONFIDENCE = 0.55


# Diagnostic-only timers. Keep placement order, routing, and image pixels unchanged.
def timing_log(event, **fields):
    suffix = " ".join(f"{key}={value}" for key, value in fields.items())
    print(f"APPENDIX4_TIMING {event} {suffix}", file=sys.stderr, flush=True)



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


def draw_callout_markers(draw, marker_points, layout_box, canvas_size,
                         obstacle_rects=None, content_integral=None,
                         labels_outside=False):
    """Collision-aware joint label placement and orthogonal leader routing.

    Target (draw_x/draw_y) is immutable. A candidate consists of BOTH its
    label rectangle and complete leader polyline; no candidate is committed
    until the pair passes the collision tests.
    """
    if not marker_points:
        return

    t_callout = time.perf_counter()
    timing_log("callout_start", markers=len(marker_points), canvas=canvas_size)
    layout_x, layout_y, layout_w, layout_h = layout_box
    canvas_w, canvas_h = canvas_size
    font_size = int(round(min(68, max(48, 42 * max(
        1.0, min(layout_w, layout_h) / 1800.0)))))
    # Regular weight, preserving the existing font size.
    # Prefer Arial Regular on Windows; use a regular-weight fallback elsewhere.
    font = None
    for font_name in ("arial.ttf", "LiberationSans-Regular.ttf", "DejaVuSans.ttf"):
        try:
            font = ImageFont.truetype(font_name, font_size)
            break
        except OSError:
            continue
    if font is None:
        font = ImageFont.load_default()
    label_h = max(68, int(round(font_size * 1.45)))
    gap = max(24, int(font_size * 0.55))  # Clear separation between label cells
    route_clearance = max(9, int(font_size * 0.18))
    preferred_leader = max(125, int(font_size * 2.7))
    min_leader = max(90, int(font_size * 1.9))
    blue, pink, white = (0, 0, 255), (255, 0, 128), (255, 255, 255)
    obstacles = obstacle_rects or []

    def color_for(label):
        return pink if label.strip().upper().startswith("V") else blue

    def intersects(a, b, clearance=0):
        return not (a[2] + clearance <= b[0] or b[2] + clearance <= a[0]
                    or a[3] + clearance <= b[1] or b[3] + clearance <= a[1])

    def expanded(box, d):
        return (box[0]-d, box[1]-d, box[2]+d, box[3]+d)

    def segment_rectangle(a, b, pad=0):
        return (min(a[0], b[0])-pad, min(a[1], b[1])-pad,
                max(a[0], b[0])+pad, max(a[1], b[1])+pad)

    def simplify(path):
        out = []
        for p in path:
            if not out or abs(p[0]-out[-1][0]) > 0.01 or abs(p[1]-out[-1][1]) > 0.01:
                out.append(p)
        i = 1
        while i < len(out)-1:
            a, b, c = out[i-1:i+2]
            if (abs(a[0]-b[0]) < 0.01 and abs(b[0]-c[0]) < 0.01) or \
               (abs(a[1]-b[1]) < 0.01 and abs(b[1]-c[1]) < 0.01):
                out.pop(i)
            else:
                i += 1
        return out

    def segments(path):
        # Stream adjacent segments without allocating a new list/slice for
        # every collision test. Iteration order and geometry are unchanged.
        return pairwise(path)

    def is_axis_aligned(a, b):
        return abs(a[0]-b[0]) < 0.01 or abs(a[1]-b[1]) < 0.01

    def path_hits_rectangle(path, rect, pad=route_clearance):
        return any(intersects(segment_rectangle(a,b,pad), rect) for a,b in segments(path))

    def segment_collision(ab, cd, clearance=route_clearance):
        # Expanded bounding boxes correctly detect crossings, shared trunks
        # and orthogonal leaders that pass too close to one another.
        return intersects(segment_rectangle(*ab, clearance),
                          segment_rectangle(*cd, 0))

    # Accurate geometry for diagonal segments: a bounding rectangle alone
    # incorrectly classifies many diagonal paths as colliding.
    def point_segment_distance(p, a, b):
        dx, dy = b[0] - a[0], b[1] - a[1]
        denom = dx * dx + dy * dy
        t = 0.0 if denom < 1e-9 else max(0.0, min(1.0,
            ((p[0]-a[0])*dx + (p[1]-a[1])*dy) / denom))
        return ((p[0]-a[0]-t*dx)**2 + (p[1]-a[1]-t*dy)**2)**0.5

    def cross(a, b, c):
        return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])

    def lines_intersect(a, b, c, d):
        eps = 1e-7
        o1, o2, o3, o4 = cross(a,b,c),cross(a,b,d),cross(c,d,a),cross(c,d,b)
        if ((o1>eps and o2<-eps) or (o1<-eps and o2>eps)) and ((o3>eps and o4<-eps) or (o3<-eps and o4>eps)):
            return True
        return (abs(o1)<=eps and point_segment_distance(c,a,b)<=eps or
                abs(o2)<=eps and point_segment_distance(d,a,b)<=eps or
                abs(o3)<=eps and point_segment_distance(a,c,d)<=eps or
                abs(o4)<=eps and point_segment_distance(b,c,d)<=eps)

    def precise_segment_collision(ab, cd, clearance=route_clearance):
        (a,b),(c,d)=ab,cd
        # Equivalent bounding-box precheck; no temporary rectangles allocated.
        if (max(a[0],b[0])+clearance <= min(c[0],d[0]) or
            max(c[0],d[0]) <= min(a[0],b[0])-clearance or
            max(a[1],b[1])+clearance <= min(c[1],d[1]) or
            max(c[1],d[1]) <= min(a[1],b[1])-clearance):
            return False
        if lines_intersect(a,b,c,d):
            return True
        return (point_segment_distance(a,c,d) < clearance or
                point_segment_distance(b,c,d) < clearance or
                point_segment_distance(c,a,b) < clearance or
                point_segment_distance(d,a,b) < clearance)

    def precise_path_hits_rectangle(path, rect, pad=route_clearance):
        x1,y1,x2,y2 = expanded(rect,pad)
        # Construct the four immutable edges once per call, avoiding tuple slicing
        # and zip/list construction for every segment of the leader.
        tl=(x1,y1); tr=(x2,y1); br=(x2,y2); bl=(x1,y2)
        for a,b in segments(path):
            if (max(a[0],b[0]) <= x1 or min(a[0],b[0]) >= x2 or
                max(a[1],b[1]) <= y1 or min(a[1],b[1]) >= y2):
                continue
            if (x1<=a[0]<=x2 and y1<=a[1]<=y2) or (x1<=b[0]<=x2 and y1<=b[1]<=y2):
                return True
            if (lines_intersect(a,b,tl,tr) or lines_intersect(a,b,tr,br) or
                lines_intersect(a,b,br,bl) or lines_intersect(a,b,bl,tl)):
                return True
        return False

    marker_tolerance = max(24, min(72, int(round(min(layout_w, layout_h)*0.018))))
    # Group only labels of the same marking type. The original code grouped
    # S20/S22/V03 and then colored the entire combined cell blue.
    typed_points = {}
    for point in marker_points:
        marker_label = str(point.get("label", "")).strip().upper()
        marker_type = "V" if marker_label.startswith("V") else (
            "S" if marker_label.startswith("S") else marker_label[:1])
        typed_points.setdefault(marker_type, []).append(point)
    groups = []
    for same_type_points in typed_points.values():
        groups.extend(group_markers(same_type_points, tolerance=marker_tolerance))
    records = []
    for group in groups:
        x, y = group.get("draw_x", group["x"]), group.get("draw_y", group["y"])
        label = ", ".join(group["labels"])
        text_w, text_h = text_size(draw, label, font)
        width = max(110, text_w + 28)
        records.append({"group": group, "target": (x,y), "label": label,
                        "text_w": text_w, "text_h": text_h, "width": width,
                        "target_box": (x-6,y-6,x+6,y+6)})
    targets = [r["target_box"] for r in records]
    timing_log("groups_ready", groups=len(records), seconds=f"{time.perf_counter()-t_callout:.3f}")

    def candidate_boxes(r):
        x, y = r["target"]
        w = r["width"]
        layout_right = layout_x + layout_w
        layout_bottom = layout_y + layout_h
        margin = 8
        outside_gap = 10
        # Always place labels in the white outer gutter, never on top of the image.
        # Order candidate sides by nearness to the target so maximum line length stays small.
        side_order = sorted(
            ("top", "bottom", "left", "right"),
            key=lambda side: {
                "top": abs(y - layout_y),
                "bottom": abs(layout_bottom - y),
                "left": abs(x - layout_x),
                "right": abs(layout_right - x),
            }[side]
        )
        # Prefer near-target positions first, then expand outward/sideways only as needed.
        lane_offsets = list(range(16))
        row_gap = label_h + gap
        col_gap = w + gap
        slide_offsets = [0, -w * 0.70, w * 0.70, -w * 1.35, w * 1.35, -w * 2.05, w * 2.05]
        options = []

        for side in side_order:
            if side == "top":
                base_top = layout_y - outside_gap - label_h
                max_rows = max(1, int((base_top - margin) // row_gap) + 1)
                for lane in range(max_rows):
                    top = base_top - lane * row_gap
                    for slide in slide_offsets:
                        left = x - w / 2 + slide
                        box = (left, top, left + w, top + label_h)
                        if box[0] < margin or box[1] < margin or box[2] > canvas_w - margin or box[3] > layout_y - 2:
                            continue
                        options.append((box, side))

            elif side == "bottom":
                base_top = layout_bottom + outside_gap
                max_rows = max(1, int((canvas_h - margin - base_top - label_h) // row_gap) + 1)
                for lane in range(max_rows):
                    top = base_top + lane * row_gap
                    for slide in slide_offsets:
                        left = x - w / 2 + slide
                        box = (left, top, left + w, top + label_h)
                        if box[0] < margin or box[1] < layout_bottom + 2 or box[2] > canvas_w - margin or box[3] > canvas_h - margin:
                            continue
                        options.append((box, side))

            elif side == "left":
                base_left = layout_x - outside_gap - w
                max_cols = max(1, int((base_left - margin) // col_gap) + 1)
                vertical_offsets = [0, -label_h * 0.85, label_h * 0.85, -label_h * 1.65, label_h * 1.65, -label_h * 2.45, label_h * 2.45]
                for lane in range(max_cols):
                    left = base_left - lane * col_gap
                    for voff in vertical_offsets:
                        top = y - label_h / 2 + voff
                        box = (left, top, left + w, top + label_h)
                        if box[0] < margin or box[1] < margin or box[2] > layout_x - 2 or box[3] > canvas_h - margin:
                            continue
                        options.append((box, side))

            else:  # right
                base_left = layout_right + outside_gap
                max_cols = max(1, int((canvas_w - margin - base_left - w) // col_gap) + 1)
                vertical_offsets = [0, -label_h * 0.85, label_h * 0.85, -label_h * 1.65, label_h * 1.65, -label_h * 2.45, label_h * 2.45]
                for lane in range(max_cols):
                    left = base_left + lane * col_gap
                    for voff in vertical_offsets:
                        top = y - label_h / 2 + voff
                        box = (left, top, left + w, top + label_h)
                        if box[0] < layout_right + 2 or box[1] < margin or box[2] > canvas_w - margin or box[3] > canvas_h - margin:
                            continue
                        options.append((box, side))

        # Final safeguard: keep only boxes fully outside the image rectangle.
        outside_only = []
        layout_rect = (layout_x, layout_y, layout_right, layout_bottom)
        for box, side in options:
            if not intersects(box, layout_rect, 0):
                outside_only.append((box, side))
        return outside_only, []

    def label_edge(box, side):
        """Connect only to the exact midpoint of the box edge facing target.

        Side refers to where the BOX is placed relative to the target.
        """
        x1, y1, x2, y2 = box
        return {"top": ((x1+x2)/2, y2),      # bottom edge center
                "bottom": ((x1+x2)/2, y1),   # top edge center
                "left": (x2, (y1+y2)/2),     # right edge center
                "right": (x1, (y1+y2)/2)}[side]  # left edge center

    def outward_ok(anchor, next_point, side):
        dx, dy = next_point[0]-anchor[0], next_point[1]-anchor[1]
        return {"top": dy >= -0.01 and abs(dx) < 0.01,
                "bottom": dy <= 0.01 and abs(dx) < 0.01,
                "left": dx >= -0.01 and abs(dy) < 0.01,
                "right": dx <= 0.01 and abs(dy) < 0.01}[side]

    def route_candidates(anchor, target, side, blockers):
        ax, ay = anchor
        tx, ty = target
        # A short perpendicular section connects to the EXACT edge midpoint.
        # The remaining leader can be diagonal (as in the reference drawing).
        direction = {"top": (0, 1), "bottom": (0, -1),
                     "left": (1, 0), "right": (-1, 0)}[side]
        stub_len = max(20, min(35, font_size * 0.55))
        stub = (ax + direction[0] * stub_len,
                ay + direction[1] * stub_len)
        paths = [[anchor, stub, target]]
        # Additional routes with a short section at either end of the diagonal.
        for step in (24, 40, 65):
            if side in ("top", "bottom"):
                paths.append([anchor,stub,(tx,ty-step if ty>stub[1] else ty+step),target])
            else:
                paths.append([anchor,stub,(tx-step if tx>stub[0] else tx+step,ty),target])
        # Orthogonal alternatives remain important in crowded spaces.
        xs = [tx, ax, (ax+tx)/2]
        ys = [ty, ay, (ay+ty)/2]
        for rect in blockers:
            xs.extend((rect[0]-route_clearance*2,rect[2]+route_clearance*2))
            ys.extend((rect[1]-route_clearance*2,rect[3]+route_clearance*2))
        for offset in (25,-25,55,-55,90,-90,140,-140):
            xs.append(tx+offset)
            ys.append(ty+offset)
        for x in sorted(set(xs),key=lambda v:abs(v-tx))[:20]:
            if 3<x<canvas_w-3:
                paths.append([anchor,stub,(x,stub[1]),(x,ty),target])
        for y in sorted(set(ys),key=lambda v:abs(v-ty))[:20]:
            if 3<y<canvas_h-3:
                paths.append([anchor,stub,(stub[0],y),(tx,y),target])
        return (simplify(path) for path in paths)

    def point_before_target(a, b, distance):
        dx, dy = b[0]-a[0], b[1]-a[1]
        length = (dx*dx+dy*dy)**0.5
        if length <= distance: return a
        factor = (length-distance)/length
        return (a[0]+dx*factor, a[1]+dy*factor)

    def crossing_count(path, placed):
        """Permit clean X crossings; prohibit collinear/shared-trunk overlap."""
        count = 0
        for record in placed:
            old_path = record["path"]
            for a, b in segments(path):
                for c, d in segments(old_path):
                    if not precise_segment_collision((a,b),(c,d),route_clearance):
                        continue
                    # Parallel or nearly parallel leaders that are too close
                    # cannot be traced reliably and are still forbidden.
                    ax, ay = b[0]-a[0], b[1]-a[1]
                    bx, by = d[0]-c[0], d[1]-c[1]
                    la = (ax*ax+ay*ay)**.5
                    lb = (bx*bx+by*by)**.5
                    if la < 1 or lb < 1:
                        continue
                    sinus = abs(ax*by-ay*bx)/(la*lb)
                    if sinus < .24:
                        return None
                    if lines_intersect(a,b,c,d):
                        count += 1
                    else:
                        # Near-miss is acceptable only with actual clearance.
                        return None
            if count > 2:
                return None
        return count

    def valid(box, path, own_index, placed):
        if any(intersects(box, r["box"], gap) for r in placed):
            return None
        if any(intersects(box, t, gap) for j,t in enumerate(targets) if j != own_index):
            return None
        if any(intersects(box, o, 2) for o in obstacles):
            return None
        if any(precise_path_hits_rectangle(path, r["box"]) for r in placed):
            return None
        if any(precise_path_hits_rectangle(path, t) for j,t in enumerate(targets) if j != own_index and (
               abs(records[j]["target"][0]-records[own_index]["target"][0]) > marker_tolerance or
               abs(records[j]["target"][1]-records[own_index]["target"][1]) > marker_tolerance)):
            return None
        for r in placed:
            if precise_path_hits_rectangle(r["path"], box):
                return None
        return crossing_count(path, placed)

    def search(index, placed, stage="outside", max_boxes=1500):
        r = records[index]
        best = None
        best_score = float("inf")
        blockers = [p["box"] for p in placed] + [t for j,t in enumerate(targets) if j != index]
        outside, inside = candidate_boxes(r)
        candidate_list = outside if stage == "outside" else inside
        t_marker_search = time.perf_counter()
        boxes_tried = 0
        routes_tried = 0
        collision_checks = 0
        for box, side in candidate_list[:max_boxes]:
            boxes_tried += 1
            if any(intersects(box,p["box"],gap) for p in placed):
                continue
            if any(intersects(box,t,gap) for j,t in enumerate(targets) if j != index):
                continue
            if any(intersects(box,o,2) for o in obstacles):
                continue
            if content_integral is not None and rect_content_ratio(
                    expanded(box,4),content_integral,canvas_w,canvas_h) > (0.06 if stage == "inside" else 0.10):
                continue
            anchor = label_edge(box,side)
            for path in route_candidates(anchor,r["target"],side,blockers):
                routes_tried += 1
                if len(path) < 2 or not outward_ok(anchor,path[1],side):
                    continue
                if len(path)>1 and precise_path_hits_rectangle(path[1:],box,0):
                    continue
                collision_checks += 1
                crossings = valid(box,path,index,placed)
                if crossings is None:
                    continue
                length = sum(((a[0]-b[0])**2+(a[1]-b[1])**2)**.5 for a,b in segments(path))
                direct = ((anchor[0]-r["target"][0])**2+(anchor[1]-r["target"][1])**2)**.5
                score = length + 32*(len(path)-2) + 750*crossings + abs(direct-preferred_leader)*.12
                if score < best_score:
                    best_score = score
                    best = {**r,"box":box,"side":side,"path":path,"crossings":crossings}
        timing_log("marker_search", label=r["label"].replace(" ", ""), stage=stage,
                   placed=len(placed), boxes=boxes_tried, routes=routes_tried,
                   collisions=collision_checks, found=int(best is not None),
                   seconds=f"{time.perf_counter()-t_marker_search:.3f}")
        return best

    # Resolve dense/long labels first, then allow a fresh ordering if blocked.
    # Never accept a collided shortest-path fallback.
    orders = [list(range(len(records))),
              sorted(range(len(records)),key=lambda i:-records[i]["width"]),
              sorted(range(len(records)),key=lambda i:records[i]["target"]),
              list(reversed(range(len(records))))]
    best_layout = []
    best_cost = float("inf")
    for order_index, order in enumerate(orders, 1):
        t_order = time.perf_counter()
        timing_log("order_start", order=order_index, groups=len(order))
        placed = []
        # Outside-only: never place text on top of the original drawing.
        for idx in order:
            result = search(idx, placed, "outside")
            if result is not None:
                placed.append(result)
        timing_log("order_end", order=order_index, placed=len(placed),
                   total=len(records), seconds=f"{time.perf_counter()-t_order:.3f}")
        cost = sum(p.get("crossings",0) for p in placed)
        if len(placed) > len(best_layout) or (len(placed)==len(best_layout) and cost < best_cost):
            best_layout, best_cost = placed, cost
        if len(best_layout)==len(records) and best_cost==0:
            break

    timing_log("placement_end", placed=len(best_layout), total=len(records),
               seconds=f"{time.perf_counter()-t_callout:.3f}")
    if len(best_layout) != len(records):
        missing = sorted(set(range(len(records))) -
                         {next(i for i,r in enumerate(records) if r is p or
                               (r["target"]==p["target"] and r["label"]==p["label"])) for p in best_layout})
        print("WARNING: no collision-free label route found for: " +
              ", ".join(records[i]["label"] for i in missing),file=sys.stderr,flush=True)
        # Do not silently publish an incomplete Appendix 4 page.
        raise RuntimeError("Cannot route all marker labels without overlap: " +
                           ", ".join(records[i]["label"] for i in missing))

    def arrowhead(path,color):
        tip,prev = path[-1],path[-2]
        dx,dy = tip[0]-prev[0],tip[1]-prev[1]
        size = 8
        if abs(dx)>abs(dy):
            sign=1 if dx>0 else -1
            tri=[tip,(tip[0]-sign*size,tip[1]-5),(tip[0]-sign*size,tip[1]+5)]
        else:
            sign=1 if dy>0 else -1
            tri=[tip,(tip[0]-5,tip[1]-sign*size),(tip[0]+5,tip[1]-sign*size)]
        draw.polygon(tri,fill=color)

    for r in best_layout:
        color=color_for(r["label"])
        path=r["path"]
        draw.line([c for p in path for c in p],fill=color,width=max(3,int(font_size/16)))
        arrowhead(path,color)
        x,y=r["target"]
        draw.ellipse((x-4,y-4,x+4,y+4),outline=color,fill=white,width=2)
    for r in best_layout:
        x1,y1,x2,y2=r["box"]
        color=color_for(r["label"])
        draw.rectangle((x1,y1,x2,y2),outline=color,fill=white,width=max(2,int(font_size/22)))
        draw.text((x1+(r["width"]-r["text_w"])/2,
                   y1+max(6,(label_h-r["text_h"])/2-2)),
                  r["label"],fill=color,font=font)


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


def render_with_dynamic_margins(base_image, marker_data, base_padding=180,
                                max_padding=2400, padding_step=160):
    """Render all callouts in outer white gutters, retrying with bigger gutters.

    Coordinates are always recalculated relative to the original image. A failed
    attempt never modifies the original image or publishes a partial result.
    """
    width, height = base_image.size
    last_error = None
    t_render = time.perf_counter()
    for attempt, pad in enumerate(range(base_padding, max_padding + 1, padding_step), 1):
        t_attempt = time.perf_counter()
        timing_log("padding_start", attempt=attempt, padding=pad, markers=len(marker_data))
        canvas = Image.new("RGB", (width + 2 * pad, height + 2 * pad), "white")
        canvas.paste(base_image, (pad, pad))
        points = marker_points_for_image(marker_data, width, height, pad)
        try:
            draw_callout_markers(
                ImageDraw.Draw(canvas), points,
                layout_box=(pad, pad, width, height),
                canvas_size=canvas.size,
                obstacle_rects=detect_compartment_rects(base_image, pad),
                content_integral=build_content_integral(canvas),
                labels_outside=True,
            )
            timing_log("padding_success", attempt=attempt, padding=pad,
                       seconds=f"{time.perf_counter()-t_attempt:.3f}",
                       total_seconds=f"{time.perf_counter()-t_render:.3f}")
            if pad > base_padding:
                print(f"APPENDIX4: outer margin expanded to {pad}px", file=sys.stderr, flush=True)
            return canvas
        except RuntimeError as exc:
            if "Cannot route all marker labels" not in str(exc):
                raise
            last_error = exc
            timing_log("padding_retry", attempt=attempt, padding=pad,
                       seconds=f"{time.perf_counter()-t_attempt:.3f}",
                       reason=str(exc).replace(" ", "_")[:250])
    timing_log("padding_failed", total_seconds=f"{time.perf_counter()-t_render:.3f}")
    raise RuntimeError(
        f"Outside-only label placement failed up to {max_padding}px margin: {last_error}"
    )


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

    marked_image = render_with_dynamic_margins(base_image, markers, base_padding=outer_pad)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    marked_image.save(output_path, dpi=(300, 300), quality=95)


def main():
    if len(sys.argv) < 4:
        print("Usage: generate_ga_marker_map.py input.json output.png placements.json", file=sys.stderr)
        sys.exit(1)

    input_json = Path(sys.argv[1])
    output_image = Path(sys.argv[2])
    placement_json = Path(sys.argv[3])

    t_main = time.perf_counter()
    data = json.loads(input_json.read_text(encoding="utf-8"))
    print("APPENDIX4: input loaded", file=sys.stderr, flush=True)

    # Area-only uploads do not have a separate common GA file. In that case,
    # use the first assigned area layout as the Appendix 4 base map instead of
    # failing the whole generation.
    area_layouts = data.get("areaLayouts") or []
    ga_source = data.get("gaLayoutPhysicalPath") or data.get("gaLayout")
    if not ga_source:
        for area_layout in area_layouts:
            candidate = area_layout.get("filePath") if isinstance(area_layout, dict) else None
            if candidate and resolve_web_path(candidate) and resolve_web_path(candidate).exists():
                ga_source = candidate
                break

    ga_path = resolve_web_path(ga_source)
    if not ga_path or not ga_path.exists():
        print(
            f"GA/area layout image not found: {ga_source} "
            f"(resolved: {ga_path})",
            file=sys.stderr
        )
        sys.exit(1)

    if not data.get("gaLayout"):
        data["gaLayout"] = ga_source

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


    # Redraw the main page with a margin that grows until outside-only labels fit.
    # Earlier area-placement percentages are independent of this final padding.
    ga_img = render_with_dynamic_margins(base_img, common_markers, base_padding=outer_pad)

    output_image.parent.mkdir(parents=True, exist_ok=True)
    ga_img.save(output_image, dpi=(300, 300), quality=95)
    print(f"APPENDIX4: GA image saved path={output_image}", file=sys.stderr, flush=True)
    timing_log("main_ga_image_saved", seconds=f"{time.perf_counter()-t_main:.3f}")

    manifest_path = output_image.parent / "appendix4-layout-pages.json"
    manifest_path.write_text(
        json.dumps(generated_layouts, indent=2),
        encoding="utf-8"
    )

    timing_log("main_total", seconds=f"{time.perf_counter()-t_main:.3f}")
    placement_json.write_text(
        json.dumps(output_placements, indent=2),
        encoding="utf-8"
    )


if __name__ == "__main__":
    main()
