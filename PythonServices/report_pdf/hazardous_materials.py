from io import BytesIO
import sys
from pathlib import Path
from urllib.parse import urlparse
import re
from pypdf import PdfReader
from PIL import Image, ImageDraw, ImageFont
import cv2
import numpy as np
import pytesseract
pytesseract.pytesseract.tesseract_cmd = r"C:\Program Files\Tesseract-OCR\tesseract.exe"
from reportlab.lib import colors
from reportlab.lib.pagesizes import A3, landscape
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfgen import canvas
from report_pdf.helpers import (
    fit_text_to_width,
    txt,
    draw_common_header_footer,
    wrap_text_to_lines,
    calculate_row_height,
)


TABLE_FONT = "Times-Roman"
TABLE_FONT_BOLD = "Times-Bold"
CONTENT_LEFT = 70
CONTENT_RIGHT_MARGIN = 70
FOOTER_LINE_Y = 55
CONTENT_BOTTOM_Y = FOOTER_LINE_Y + 10
FIRST_PAGE_CONTENT_TOP_OFFSET = 125
NEXT_PAGE_CONTENT_TOP_OFFSET = 85
SECTION_LABEL_GAP = 14
SECTION_BOTTOM_GAP = 25

HAZARD_LAYOUT_PAGE_WIDTH = 840
HAZARD_LAYOUT_PAGE_HEIGHT = 580


def normalize_web_path(value):
    value = str(value or "").strip().replace("\\", "/")
    if not value:
        return ""

    parsed = urlparse(value)
    if parsed.scheme or parsed.netloc:
        value = parsed.path

    return "/" + value.lstrip("/")


def resolve_report_image_path(web_path):
    value = str(web_path or "").strip()
    if not value:
        return None

    path = Path(value)
    if path.exists():
        return path

    if len(value) > 2 and value[1] == ":":
        return path

    parsed = urlparse(value)
    if parsed.scheme or parsed.netloc:
        value = parsed.path

    path = Path(value)
    if path.exists():
        return path

    python_services_dir = Path(__file__).resolve().parents[1]
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


def first_value(row, *keys):
    for key in keys:
        value = row.get(key)
        if value is not None and str(value).strip():
            return str(value).strip()
    return ""


def equipment_sample_no(row):
    return first_value(
        row,
        "sample_No",
        "Sample_No",        
    )


def equipment_name(row):
    return first_value(
        row,
        "equipmentName",
        "EquipmentName",
    )


def equipment_hazardous_material(row):
    return first_value(
        row,
        "hazardousMaterial",
        "HazardousMaterial",
        "materialClassification",
        "MaterialClassification",
        
    )


def hazard_marker_label(row):
    name = equipment_name(row)
    material = equipment_hazardous_material(row)

    if name and material:
        return f"{name} containing {material}"
    if name:
        return f"{name} containing"
    if material:
        return f"Containing {material}"
    return equipment_sample_no(row)


def marker_label(marker):
    return first_value(marker, "markerLabel", "MarkerLabel", "sampleNo", "Sample_No")


def marker_image_path(marker):
    return first_value(marker, "imagePath", "ImagePath")

def normalize_sample_label(value):
    return (
        str(value or "")
        .strip()
        .upper()
        .replace(" ", "")
        .replace("-", "")
        .replace("_", "")
    )


def get_ga_layout_source(data):
    ga_layout_source = (
        data.get("gaLayout")
        or data.get("GaLayout")
        or data.get("gaLayoutPhysicalPath")
        or data.get("GaLayoutPhysicalPath")
    )

    if isinstance(ga_layout_source, dict):
        ga_layout_source = (
            ga_layout_source.get("filePath")
            or ga_layout_source.get("FilePath")
        )

    return ga_layout_source
_SAMPLE_LABEL_RE = re.compile(r"^[SV]\d{1,3}$", re.IGNORECASE)


def detect_existing_sample_text_markings(image_path, expected_labels=None):
    expected = {
        normalize_sample_label(label)
        for label in (expected_labels or [])
        if normalize_sample_label(label)
    }

    image = Image.open(image_path).convert("RGB")
    image_w, image_h = image.size
    detected = {}

    variants = []

    variants.append(image)

    gray = image.convert("L")
    variants.append(gray.convert("RGB"))

    for variant in variants:
        for scale in (3,):
            enlarged = variant.resize((image_w * scale, image_h * scale))

            for psm in (6,):
                data = pytesseract.image_to_data(
                    enlarged,
                    config=(
                        f"--psm {psm} "
                        "-c tessedit_char_whitelist=ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_ "
                        "-c preserve_interword_spaces=1"
                    ),
                    output_type=pytesseract.Output.DICT,
                    timeout=8,
                )

                words = []
                for i, raw_text in enumerate(data.get("text", [])):
                    text = str(raw_text or "").strip()
                    if not text:
                        continue

                    words.append({
                        "text": text,
                        "label": normalize_sample_label(text),
                        "x": data["left"][i] / scale,
                        "y": data["top"][i] / scale,
                        "w": data["width"][i] / scale,
                        "h": data["height"][i] / scale,
                    })

                for i in range(len(words)):
                    for take in (1, 2, 3):
                        chunk = words[i:i + take]
                        if len(chunk) != take:
                            continue

                        joined = "".join(item["text"] for item in chunk)
                        label = normalize_sample_label(joined)

                        if expected and label not in expected:
                            continue

                        if not _SAMPLE_LABEL_RE.search(label):
                            continue

                        x1 = min(item["x"] for item in chunk)
                        y1 = min(item["y"] for item in chunk)
                        x2 = max(item["x"] + item["w"] for item in chunk)
                        y2 = max(item["y"] + item["h"] for item in chunk)

                        detected[label] = {
                            "label": label,
                            "xPercent": ((x1 + x2) / 2) / image_w,
                            "yPercent": ((y1 + y2) / 2) / image_h,
                            "widthPercent": (x2 - x1) / image_w,
                            "heightPercent": (y2 - y1) / image_h,
                            "source": "existing-image",
                        }

    return list(detected.values())


def detect_all_existing_sample_markings(image_path, expected_labels=None):
    by_label = {}

    for item in detect_existing_blue_sample_markings(image_path):
        label = normalize_sample_label(item.get("label"))
        if label:
            by_label[label] = item

    for item in detect_existing_sample_text_markings(image_path, expected_labels):
        label = normalize_sample_label(item.get("label"))
        if label:
            by_label[label] = item

    return list(by_label.values())


def collect_layout_sources_for_hazard_detection(data):
    sources = []

    def add_source(value):
        if isinstance(value, dict):
            value = value.get("filePath") or value.get("FilePath")
        value = str(value or "").strip()
        if not value:
            return

        normalized = normalize_web_path(value)
        existing = {normalize_web_path(item) for item in sources}
        if normalized not in existing:
            sources.append(value)

    add_source(get_ga_layout_source(data))
    add_source(data.get("latestAreaLayout") or data.get("LatestAreaLayout"))

    for layout in data.get("areaLayouts") or data.get("AreaLayouts") or []:
        add_source(layout)

    job_id = (data.get("job") or data.get("Job") or {}).get("id") or (data.get("job") or data.get("Job") or {}).get("Id")
    if job_id:
        report_folder = Path.cwd() / "wwwroot" / "reports" / "ihm" / str(job_id)
        add_source(str(report_folder / "appendix4-ga-marker-map.png"))

        for image_path in sorted(report_folder.glob("appendix4-area-*.png")):
            add_source(str(image_path))

    return sources

def detect_existing_blue_sample_markings(image_path):
    image = Image.open(image_path).convert("RGB")
    arr = np.array(image)

    red = arr[:, :, 0]
    green = arr[:, :, 1]
    blue = arr[:, :, 2]

    blue_mask = (
        (blue > 110) &
        (red < 170) &
        (green < 190) &
        ((blue - red) > 25) &
        ((blue - green) > 15)
    )

    red_mask = (
            (red > 140) &
            (green < 140) &
            (blue < 140) &
            ((red - green) > 35) &
            ((red - blue) > 35)
        )

    dark_text_mask = (
            (red < 80) &
            (green < 80) &
            (blue < 80)
        )

    mask = (blue_mask | red_mask | dark_text_mask).astype(np.uint8) * 255

    # Connect blue text/box/line/dot into one marking region.
    kernel = np.ones((9, 9), np.uint8)
    connected = cv2.dilate(mask, kernel, iterations=2)

    contours, _ = cv2.findContours(
        connected,
        cv2.RETR_EXTERNAL,
        cv2.CHAIN_APPROX_SIMPLE
    )

    image_h, image_w = arr.shape[:2]
    detected = []

    for contour in contours:
        x, y, w, h = cv2.boundingRect(contour)

        if w < 25 or h < 15:
            continue

        # Ignore very large accidental regions.
        if w > image_w * 0.45 or h > image_h * 0.45:
            continue

        pad = 8
        x1 = max(0, x - pad)
        y1 = max(0, y - pad)
        x2 = min(image_w, x + w + pad)
        y2 = min(image_h, y + h + pad)

        crop = image.crop((x1, y1, x2, y2))

        crop = crop.resize((crop.width * 4, crop.height * 4))

        text = ""

        for psm in (6, 7, 11):
            text += "\n" + pytesseract.image_to_string(
                crop,
                config=(
                    f"--psm {psm} "
                    "-c tessedit_char_whitelist=ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789,-_"
                )
            )

        labels = []

        for part in re.split(r"[\s,;/|]+", text.replace("\n", " ")):
            label = normalize_sample_label(part)
            if _SAMPLE_LABEL_RE.search(label):
                labels.append(label)

        for label in labels:
            # For vertical callouts, point is usually at the image-side end.
            center_x = x + (w / 2)

            if y < image_h * 0.25:
                point_y = y + h
            elif y > image_h * 0.60:
                point_y = y
            else:
                point_y = y + (h / 2)

            detected.append({
                "label": label,
                "xPercent": center_x / image_w,
                "yPercent": point_y / image_h,
                "widthPercent": w / image_w,
                "heightPercent": h / image_h,
                "source": "existing-image",
            })

    return detected

def dedupe_latest_markers(markers):
    latest = {}

    for marker in markers:
        if marker.get("suppressRender") or marker.get("SuppressRender"):
            continue

        label = normalize_sample_label(marker_label(marker))
        image_path = normalize_web_path(marker_image_path(marker))
        if not label or not image_path:
            continue

        key = (label, image_path)
        marker_id = int(marker.get("id") or marker.get("Id") or 0)

        existing = latest.get(key)
        existing_id = int(existing.get("id") or existing.get("Id") or 0) if existing else -1

        existing_source = str((existing or {}).get("source") or (existing or {}).get("Source") or "").strip().lower()
        marker_source = str(marker.get("source") or marker.get("Source") or "").strip().lower()

        if existing is None:
            latest[key] = marker
        elif existing_source == "existing-image":
            continue
        elif marker_source == "existing-image":
            latest[key] = marker
        elif marker_id >= existing_id:
            latest[key] = marker

    return list(latest.values())


def wrap_pil_text(draw, text, font, max_width):
    words = str(text or "").split()
    lines = []
    current = ""

    for word in words:
        test = word if not current else f"{current} {word}"
        box = draw.textbbox((0, 0), test, font=font)

        if box[2] - box[0] <= max_width:
            current = test
            continue

        if current:
            lines.append(current)
        current = word

    if current:
        lines.append(current)

    return lines or [""]
def erase_existing_blue_markings(image, protected_markers=None):
    arr = np.array(image.convert("RGB"))
    image_h, image_w = arr.shape[:2]

    red = arr[:, :, 0].astype(np.int16)
    green = arr[:, :, 1].astype(np.int16)
    blue = arr[:, :, 2].astype(np.int16)

    # Strict marker-blue only. Do not catch room fills, black room text, or pale layout colors.
    blue_mask = (
        (blue > 145) &
        (red < 90) &
        (green < 120) &
        ((blue - red) > 70) &
        ((blue - green) > 45)
    ).astype(np.uint8) * 255

    # Join only broken blue callout strokes. Keep kernel small so room contents are not joined.
    connect_kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (5, 5))
    connected_mask = cv2.dilate(blue_mask, connect_kernel, iterations=1)

    num_labels, labels, stats, _ = cv2.connectedComponentsWithStats(
        connected_mask,
        connectivity=8
    )

    protected_mask = np.zeros_like(blue_mask)

    for marker in protected_markers or []:
        px = int(percent_to_unit(marker.get("xPercent") or marker.get("XPercent")) * image_w)
        py = int(percent_to_unit(marker.get("yPercent") or marker.get("YPercent")) * image_h)

        px = max(0, min(px, image_w - 1))
        py = max(0, min(py, image_h - 1))

        # OCR coordinate is sample-number/label position, not dot position.
        # Protect only nearby connected blue component, not every old marking.
        search_radius_x = 130
        search_radius_y = 90

        x1 = max(0, px - search_radius_x)
        y1 = max(0, py - search_radius_y)
        x2 = min(image_w, px + search_radius_x)
        y2 = min(image_h, py + search_radius_y)

        nearby = labels[y1:y2, x1:x2]
        nearby_ids = [
            int(v)
            for v in np.unique(nearby)
            if int(v) != 0
        ]

        if not nearby_ids:
            continue

        component_id = max(
            nearby_ids,
            key=lambda cid: stats[cid, cv2.CC_STAT_AREA]
        )

        component_area = stats[component_id, cv2.CC_STAT_AREA]

        # Avoid protecting a huge accidental region.
        if component_area > image_w * image_h * 0.03:
            continue

        component_mask = (labels == component_id).astype(np.uint8) * 255
        protected_mask = cv2.bitwise_or(
            protected_mask,
            cv2.bitwise_and(blue_mask, component_mask)
        )

    erase_mask = cv2.bitwise_and(blue_mask, cv2.bitwise_not(protected_mask))

    # Erase only marker-blue strokes. No inpaint: inpaint was damaging room text/fills.
    erase_mask = cv2.dilate(
        erase_mask,
        cv2.getStructuringElement(cv2.MORPH_RECT, (2, 2)),
        iterations=1
    )

    cleaned = arr.astype(np.uint8).copy()
    cleaned[erase_mask > 0] = [255, 255, 255]

    return Image.fromarray(cleaned)

def percent_to_unit(value):
    try:
        number = float(value or 0)
    except (TypeError, ValueError):
        return 0.0

    if number > 1:
        number = number / 100.0

    return max(0.0, min(number, 1.0))
def draw_hazard_marked_layout_image(source_path, marker_items, output_path, bottom_dot_offset=0):
    base = Image.open(source_path).convert("RGB")

    protected_markers = [
        item["marker"]
        for item in marker_items
        if (
            "ocr-existing-label" in str(item["marker"].get("notes") or item["marker"].get("Notes") or "").strip().lower()
            or str(item["marker"].get("source") or item["marker"].get("Source") or "").strip().lower() == "existing-image"
        )
    ]

    base = erase_existing_blue_markings(base, protected_markers)
    image_w, image_h = base.size

    outer_pad = 220
    canvas_img = Image.new(
        "RGB",
        (image_w + outer_pad * 2, image_h + outer_pad * 2),
        (255, 255, 255)
    )
    canvas_img.paste(base, (outer_pad, outer_pad))

    draw = ImageDraw.Draw(canvas_img)

    try:
        font = ImageFont.truetype("arial.ttf", 16)
    except OSError:
        font = ImageFont.load_default()

    blue = (0, 0, 255)
    white = (255, 255, 255)

    placements = []
    used_boxes = []
    label_h_base = 28
    gap = 12
    line_gap = 28
    margin = 8

    layout_x = outer_pad
    layout_y = outer_pad
    layout_right = outer_pad + image_w
    layout_bottom = outer_pad + image_h

    for item in marker_items:
        marker = item["marker"]
        label = item["label"]

        notes = str(marker.get("notes") or marker.get("Notes") or "").strip().lower()
        marker_source = str(marker.get("source") or marker.get("Source") or "").strip().lower()
        is_existing_label = "ocr-existing-label" in notes or marker_source == "existing-image"

        x = outer_pad + percent_to_unit(marker.get("xPercent") or marker.get("XPercent")) * image_w
        y = outer_pad + percent_to_unit(marker.get("yPercent") or marker.get("YPercent")) * image_h

        if is_existing_label:
            max_label_w = 210
            lines = wrap_pil_text(draw, label, font, max_label_w - 14)
            line_h = 18

            label_w = max(
                90,
                min(
                    max_label_w,
                    max(draw.textbbox((0, 0), line, font=font)[2] for line in lines) + 14
                )
            )
            label_h = max(label_h_base, len(lines) * line_h + 10)

            box_x = x - label_w / 2
            box_y = y - label_h / 2

            box_x = max(margin, min(box_x, canvas_img.size[0] - label_w - margin))
            box_y = max(margin, min(box_y, canvas_img.size[1] - label_h - margin))

            # OCR x/y is the existing label text position. Replace text only.
            # Do not redraw dot, leader line, or full label border.
            
            text_clear_pad_x = max(label_w / 2, 55)
            text_clear_pad_y = max(label_h / 2, 18)

            clear_x1 = max(layout_x, x - text_clear_pad_x)
            clear_y1 = max(layout_y, y - text_clear_pad_y)
            clear_x2 = min(layout_right, x + text_clear_pad_x)
            clear_y2 = min(layout_bottom, y + text_clear_pad_y)

            draw.rectangle(
                [clear_x1, clear_y1, clear_x2, clear_y2],
                outline=blue,
                fill=white,
                width=2,
            )

            text_y = clear_y1 + 4
            for line in lines:
                draw.text((clear_x1 + 5, text_y), line, fill=blue, font=font)
                text_y += line_h

            used_boxes.append((clear_x1, clear_y1, clear_x2, clear_y2))
            continue


        max_label_w = 210
        lines = wrap_pil_text(draw, label, font, max_label_w - 14)
        line_h = 18
        label_w = max(
            90,
            min(
                max_label_w,
                max(draw.textbbox((0, 0), line, font=font)[2] for line in lines) + 14
            )
        )
        label_h = max(label_h_base, len(lines) * line_h + 10)

        candidate_sides = sorted(
            [
                ("left", x - layout_x),
                ("right", layout_right - x),
                ("top", y - layout_y),
                ("bottom", layout_bottom - y),
            ],
            key=lambda item: item[1]
        )

        def make_candidate_box(candidate_side):
            if candidate_side == "top":
                return (
                    x - label_w / 2,
                    layout_y - line_gap - label_h
                )
            if candidate_side == "bottom":
                return (
                    x - label_w / 2,
                    layout_bottom + line_gap
                )
            if candidate_side == "left":
                return (
                    layout_x - line_gap - label_w,
                    y - label_h / 2
                )
            return (
                layout_right + line_gap,
                y - label_h / 2
            )

        side = candidate_sides[0][0]
        box_x, box_y = make_candidate_box(side)

        for candidate_side, _ in candidate_sides:
            test_x, test_y = make_candidate_box(candidate_side)
            test_x = max(margin, min(test_x, canvas_img.size[0] - label_w - margin))
            test_y = max(margin, min(test_y, canvas_img.size[1] - label_h - margin))

            test_box = (test_x, test_y, test_x + label_w, test_y + label_h)
            has_overlap = any(
                not (
                    test_box[2] + gap < old_x1 or
                    test_box[0] - gap > old_x2 or
                    test_box[3] + gap < old_y1 or
                    test_box[1] - gap > old_y2
                )
                for old_x1, old_y1, old_x2, old_y2 in used_boxes
            )

            if not has_overlap:
                side = candidate_side
                box_x = test_x
                box_y = test_y
                break

        box_x = max(margin, min(box_x, canvas_img.size[0] - label_w - margin))
        box_y = max(margin, min(box_y, canvas_img.size[1] - label_h - margin))

                # Push label away from existing labels, but never loop forever.
        for _ in range(30):
            x1, y1, x2, y2 = box_x, box_y, box_x + label_w, box_y + label_h
            moved = False

            for old_x1, old_y1, old_x2, old_y2 in used_boxes:
                overlaps = not (
                    x2 + gap < old_x1 or
                    x1 - gap > old_x2 or
                    y2 + gap < old_y1 or
                    y1 - gap > old_y2
                )
                if not overlaps:
                    continue

                old_box_y = box_y

                if side == "top":
                    box_y = old_y1 - label_h - gap
                else:
                    box_y = old_y2 + gap

                box_y = max(margin, min(box_y, canvas_img.size[1] - label_h - margin))

                moved = abs(box_y - old_box_y) > 0.1
                break

            if not moved:
                break

        draw_x = x
        draw_y = y

        if side == "top":
            line_end = (x, box_y + label_h)
        elif side == "bottom":
            line_end = (x, box_y)
        elif side == "left":
            draw_y = y
            line_end = (box_x + label_w, y)
        else:
            draw_y = y
            line_end = (box_x, y)
        box = (box_x, box_y, box_x + label_w, box_y + label_h)
        used_boxes.append(box)

        placements.append({
            "x": draw_x,
            "y": draw_y,
            "box": box,
            "line_end": line_end,
            "lines": lines,
            "label_h": label_h,
        })

    for item in placements:
        x = item["x"]
        y = item["y"]
        line_end = item["line_end"]

        draw.line([x, y, line_end[0], line_end[1]], fill=blue, width=2)

        dot_radius = 4
        draw.ellipse(
            [x - dot_radius, y - dot_radius, x + dot_radius, y + dot_radius],
            outline=blue,
            fill=white,
            width=2,
        )

    for item in placements:
        x1, y1, x2, y2 = item["box"]
        draw.rectangle([x1, y1, x2, y2], outline=blue, fill=white, width=2)

        text_y = y1 + 6
        for line in item["lines"]:
            draw.text((x1 + 7, text_y), line, fill=blue, font=font)
            text_y += 18

    output_path.parent.mkdir(parents=True, exist_ok=True)
    canvas_img.save(output_path)
    return output_path


def build_i2_hazard_layout_images(data):
    equipment_rows = get_rows(data, "equipmentAndMachinery", "equipment")
    markers = dedupe_latest_markers(data.get("markers") or [])
    markers_by_label = {}

    for marker in markers:
        label = normalize_sample_label(marker_label(marker))
        if label:
            markers_by_label.setdefault(label, []).append(marker)

    grouped_by_image = {}
    i2_rows_by_sample_no = {}

    for row in equipment_rows:
        sample_no = normalize_sample_label(equipment_sample_no(row))
        if not sample_no:
            continue
        i2_rows_by_sample_no[sample_no] = row
        row_markers = markers_by_label.get(sample_no) or []
        if not row_markers:
            continue

        label = hazard_marker_label(row)

        for marker in row_markers:
            image_path = marker_image_path(marker)
            normalized = normalize_web_path(image_path)

            if not normalized:
                continue

            grouped_by_image.setdefault(normalized, {
                "source": image_path,
                "items": []
            })
            grouped_by_image[normalized]["items"].append({
                "marker": marker,
                "label": label,
            })
    ga_layout_source = get_ga_layout_source(data)
    ga_layout_path = resolve_report_image_path(ga_layout_source)
    normalized_ga_source = normalize_web_path(ga_layout_source)
    layout_sources_for_ocr = collect_layout_sources_for_hazard_detection(data)

    db_common_marker_sample_nos = set()
    for marker in markers:
        marker_sample = normalize_sample_label(marker_label(marker))
        marker_image = normalize_web_path(marker_image_path(marker))

        if marker_sample and marker_image == normalized_ga_source:
            db_common_marker_sample_nos.add(marker_sample)

    missing_sample_nos = [
        sample_no
        for sample_no in i2_rows_by_sample_no
        if sample_no not in db_common_marker_sample_nos
    ]

    if missing_sample_nos:
        found_existing_sample_nos = set()

        for layout_source in layout_sources_for_ocr:
            layout_path = resolve_report_image_path(layout_source)
            if not layout_path or not layout_path.exists():
                continue

            normalized_layout_source = normalize_web_path(layout_source)
            ocr_markings = detect_all_existing_sample_markings(
                layout_path,
                expected_labels=missing_sample_nos
            )
            print(
                f"Hazard OCR source: {layout_path} detected: "
                + ", ".join(sorted(normalize_sample_label(x.get("label")) for x in ocr_markings)),
                file=sys.stderr
            )
            ocr_by_label = {
                normalize_sample_label(item["label"]): item
                for item in ocr_markings
            }

            for sample_no in missing_sample_nos:
                if sample_no in found_existing_sample_nos:
                    continue

                ocr_item = ocr_by_label.get(sample_no)
                if not ocr_item:
                    continue

                row = i2_rows_by_sample_no[sample_no]
                label = hazard_marker_label(row)

                grouped_by_image.setdefault(normalized_layout_source, {
                    "source": layout_source,
                    "items": []
                })

                grouped_by_image[normalized_layout_source]["items"].append({
                    "marker": {
                        "markerLabel": sample_no,
                        "imagePath": layout_source,
                        "xPercent": ocr_item["xPercent"],
                        "yPercent": ocr_item["yPercent"],
                        "widthPercent": ocr_item.get("widthPercent"),
                        "heightPercent": ocr_item.get("heightPercent"),
                        "source": "existing-image",
                        "notes": "ocr-existing-label",
                    },
                    "label": label,
                })

                found_existing_sample_nos.add(sample_no)

        skipped = [
            sample_no
            for sample_no in missing_sample_nos
            if sample_no not in found_existing_sample_nos
        ]

        if skipped:
            message = (
                "I-2 hazard layout skipped samples without marker position: "
                + ", ".join(skipped)
            )

            print(message, file=sys.stderr)

            log_path = Path.cwd() / "wwwroot" / "reports" / "ihm" / "hazard-ocr-log.txt"
            log_path.parent.mkdir(parents=True, exist_ok=True)
            old_log = log_path.read_text(encoding="utf-8") if log_path.exists() else ""
            log_path.write_text(old_log + "\n" + message, encoding="utf-8")

    generated = []
    print(f"I-2 hazard layout image groups: {len(grouped_by_image)}", file=sys.stderr)
    output_dir = Path.cwd() / "wwwroot" / "reports" / "ihm" / str((data.get("job") or {}).get("id") or "0") / "i2-hazard-layouts"

    for index, group in enumerate(grouped_by_image.values(), start=1):
        source_path = resolve_report_image_path(group["source"])
        if not source_path or not source_path.exists():
            continue

        ga_layout_source = (
            data.get("gaLayout")
            or data.get("GaLayout")
        )

        if isinstance(ga_layout_source, dict):
            ga_layout_source = (
                ga_layout_source.get("filePath")
                or ga_layout_source.get("FilePath")
            )

        current_source = normalize_web_path(group["source"])
        common_source = normalize_web_path(ga_layout_source)

        bottom_dot_offset = 0

        output_path = output_dir / f"i2-hazard-layout-{index}.png"

        generated.append(draw_hazard_marked_layout_image(
            source_path,
            group["items"],
            output_path,
            bottom_dot_offset=bottom_dot_offset
        ))

    return generated


def make_i2_hazard_layout_pages(data, vessel_name, company_name):
    images = build_i2_hazard_layout_images(data)
    pages = []

    for image_index, image_path in enumerate(images):
        packet = BytesIO()
        c = canvas.Canvas(
            packet,
            pagesize=(HAZARD_LAYOUT_PAGE_WIDTH, HAZARD_LAYOUT_PAGE_HEIGHT)
        )

        draw_common_header_footer(
            c,
            vessel_name=vessel_name,
            company_name=company_name,
            page_no_text="Page | "
        )
        if image_index == 0:
            title = "LOCATION DIAGRAM OF HAZARDOUS MATERIALS"
            title_font = TABLE_FONT_BOLD
            title_size = 12
            title_y = HAZARD_LAYOUT_PAGE_HEIGHT - 92
            title_w = pdfmetrics.stringWidth(title, title_font, title_size)

            c.setFont(title_font, title_size)
            c.setFillColor(colors.black)
            c.drawString((HAZARD_LAYOUT_PAGE_WIDTH - title_w) / 2, title_y, title)
            c.line(
                (HAZARD_LAYOUT_PAGE_WIDTH - title_w) / 2,
                title_y - 2,
                (HAZARD_LAYOUT_PAGE_WIDTH + title_w) / 2,
                title_y - 2
            )
        usable_x = 45
        usable_y = 75
        usable_w = HAZARD_LAYOUT_PAGE_WIDTH - 90
        usable_h = HAZARD_LAYOUT_PAGE_HEIGHT - 180

        img = Image.open(image_path)
        img_w, img_h = img.size
        scale = min(usable_w / img_w, usable_h / img_h)
        draw_w = img_w * scale
        draw_h = img_h * scale

        c.drawImage(
            str(image_path),
            (HAZARD_LAYOUT_PAGE_WIDTH - draw_w) / 2,
            usable_y + (usable_h - draw_h) / 2,
            width=draw_w,
            height=draw_h,
            preserveAspectRatio=True,
            mask="auto"
        )

        c.save()
        packet.seek(0)
        pages.extend(list(PdfReader(packet).pages))

    return pages

def get_rows(data, *keys):
    for key in keys:
        rows = data.get(key)
        if isinstance(rows, list):
            return rows
    return []

def draw_centered_wrapped_text(c, text, x1, y_bottom, x2, y_top, font=TABLE_FONT, size=12, bold=False):
    value = txt(text) or "-"
    use_font = TABLE_FONT_BOLD if bold else font
    max_width = x2 - x1 - 6

    lines = wrap_text_to_lines(value, use_font, size, max_width)

    leading = size + 2
    cell_h = y_top - y_bottom
    total_h = len(lines) * leading
    start_y = y_bottom + (cell_h - total_h) / 2 + (len(lines) - 1) * leading + 1

    c.setFont(use_font, size)
    c.setFillColor(colors.black)

    for index, line in enumerate(lines):
        line_w = pdfmetrics.stringWidth(line, use_font, size)
        x = x1 + ((x2 - x1) - line_w) / 2
        y = start_y - (index * leading)
        c.drawString(x, y, line)


def draw_table_grid(c, x, y_top, col_widths, row_heights):
    total_width = sum(col_widths)
    y = y_top

    c.setStrokeColor(colors.black)
    c.setLineWidth(0.5)

    for row_h in row_heights:
        c.rect(x, y - row_h, total_width, row_h, stroke=1, fill=0)
        running_x = x
        for col_w in col_widths[:-1]:
            running_x += col_w
            c.line(running_x, y - row_h, running_x, y)
        y -= row_h


def draw_title(c, page_width, page_height):
    title = "PART I - HAZARDOUS MATERIALS CONTAINED IN THE SHIP'S STRUCTURE AND EQUIPMENT"
    c.setFont(TABLE_FONT_BOLD, 14)
    title_w = pdfmetrics.stringWidth(title, TABLE_FONT_BOLD, 14)
    c.drawString((page_width - title_w) / 2, page_height - 100, title)


def draw_section_label(c, x, y, text):
    c.setFont(TABLE_FONT_BOLD, 12)
    c.setFillColor(colors.black)
    c.drawString(x, y, text)

def get_x_positions(x, col_widths):
    x_positions = [x]
    for width in col_widths:
        x_positions.append(x_positions[-1] + width)
    return x_positions


def draw_horizontal(c, x1, x2, y):
    c.line(x1, y, x2, y)


def draw_vertical(c, x, y1, y2):
    c.line(x, y1, x, y2)

def get_paint_row_values(row, index):
    if row is None:
        return ["-", "-", "-", "-", "-", "-", "-", "-"]

    return [
        str(index),
        row.get("applicationOfPaint"),
        row.get("nameOfPaint"),
        row.get("location"),
        row.get("materialClassification"),
        row.get("mass"),
        row.get("unit"),
        row.get("remarks"),
    ]


def get_equipment_row_values(row, index):
    if row is None:
        return ["-", "-", "-", "-", "-", "-", "-", "-", "-"]

    return [
        str(index),
        row.get("equipmentName"),
        row.get("location"),
        row.get("materialClassification"),
        row.get("partsWereUsed"),
        row.get("unitKg"),
        row.get("quantityNo"),
        row.get("totalKg"),
        row.get("remarks"),
    ]


def get_structure_row_values(row, index):
    if row is None:
        return ["-", "-", "-", "-", "-", "-", "-"]

    return [
        str(index),
        row.get("equipmentName"),
        row.get("location"),
        row.get("materialClassification"),
        row.get("partsWereUsed"),
        row.get("approximateQuantity"),
        row.get("remarks"),
    ]

def draw_paint_table(c, x, y_top, rows, start_no=1):
    col_widths = [42, 155, 165, 115, 250, 75, 75, 173]
    header_top_h = 31
    header_sub_h = 22
    header_h = header_top_h + header_sub_h
    display_rows = rows if rows else [None]
    x_positions = get_x_positions(x, col_widths)

    row_values = [
        get_paint_row_values(row, index)
        for index, row in enumerate(display_rows, start=start_no)
    ]

    row_heights = [
        calculate_row_height(values, col_widths, min_height=25, font=TABLE_FONT, size=12)
        for values in row_values
    ]

    table_bottom = y_top - header_h - sum(row_heights)

    c.setStrokeColor(colors.black)
    c.setLineWidth(0.5)
    c.rect(x, table_bottom, sum(col_widths), y_top - table_bottom, stroke=1, fill=0)

    for col_index, x_pos in enumerate(x_positions[1:-1], start=1):
        if col_index == 6:
            draw_vertical(c, x_pos, table_bottom, y_top - header_top_h)
        else:
            draw_vertical(c, x_pos, table_bottom, y_top)

    draw_horizontal(c, x, x_positions[-1], y_top - header_h)
    draw_horizontal(c, x_positions[5], x_positions[7], y_top - header_top_h)

    y_line = y_top - header_h
    for row_height in row_heights[:-1]:
        y_line -= row_height
        draw_horizontal(c, x, x_positions[-1], y_line)

    headers = [
        ("No.", 0, 1),
        ("Application of Paint", 1, 2),
        ("Name of Paint", 2, 3),
        ("Location", 3, 4),
        ("Materials (classification in appendix 1)", 4, 5),
        ("Approximate Quantity", 5, 7),
        ("Remarks", 7, 8),
    ]

    for label, left, right in headers:
        if label == "Approximate Quantity":
            draw_centered_wrapped_text(c, label, x_positions[left], y_top - header_top_h, x_positions[right], y_top, size=12, bold=True)
        else:
            draw_centered_wrapped_text(c, label, x_positions[left], y_top - header_h, x_positions[right], y_top, size=12, bold=True)

    draw_centered_wrapped_text(c, "Mass", x_positions[5], y_top - header_h, x_positions[6], y_top - header_top_h, size=12, bold=True)
    draw_centered_wrapped_text(c, "Unit", x_positions[6], y_top - header_h, x_positions[7], y_top - header_top_h, size=12, bold=True)

    y = y_top - header_h
    for values, row_height in zip(row_values, row_heights):
        row_bottom = y - row_height

        for col_index, value in enumerate(values):
            draw_centered_wrapped_text(c, value, x_positions[col_index], row_bottom, x_positions[col_index + 1], y, size=12)

        y = row_bottom

    return y

def draw_equipment_table(c, x, y_top, rows, start_no=1):
    col_widths = [42, 195, 170, 190, 115, 78, 70, 78, 112]
    header_top_h = 31
    header_sub_h = 22
    header_h = header_top_h + header_sub_h
    display_rows = rows if rows else [None]
    x_positions = get_x_positions(x, col_widths)

    row_values = [
        get_equipment_row_values(row, index)
        for index, row in enumerate(display_rows, start=start_no)
    ]

    row_heights = [
        calculate_row_height(values, col_widths, min_height=25, font=TABLE_FONT, size=12)
        for values in row_values
    ]

    table_bottom = y_top - header_h - sum(row_heights)

    c.setStrokeColor(colors.black)
    c.setLineWidth(0.5)
    c.rect(x, table_bottom, sum(col_widths), y_top - table_bottom, stroke=1, fill=0)

    for col_index, x_pos in enumerate(x_positions[1:-1], start=1):
        if col_index in [6, 7]:
            draw_vertical(c, x_pos, table_bottom, y_top - header_top_h)
        else:
            draw_vertical(c, x_pos, table_bottom, y_top)

    draw_horizontal(c, x, x_positions[-1], y_top - header_h)
    draw_horizontal(c, x_positions[5], x_positions[8], y_top - header_top_h)

    y_line = y_top - header_h
    for row_height in row_heights[:-1]:
        y_line -= row_height
        draw_horizontal(c, x, x_positions[-1], y_line)

    headers = [
        "No.",
        "Name of equipment and machinery",
        "Location",
        "Materials\n(classification in appendix 1)",
        "Parts were used",
        "Approximate quantity",
        "Remarks",
    ]

    header_spans = [(0, 1), (1, 2), (2, 3), (3, 4), (4, 5), (5, 8), (8, 9)]

    for label, span in zip(headers, header_spans):
        if label == "Approximate quantity":
            draw_centered_wrapped_text(c, label, x_positions[span[0]], y_top - header_top_h, x_positions[span[1]], y_top, size=12, bold=True)
        else:
            draw_centered_wrapped_text(c, label, x_positions[span[0]], y_top - header_h, x_positions[span[1]], y_top, size=12, bold=True)

    draw_centered_wrapped_text(c, "Unit (Kg)", x_positions[5], y_top - header_h, x_positions[6], y_top - header_top_h, size=12, bold=True)
    draw_centered_wrapped_text(c, "No.", x_positions[6], y_top - header_h, x_positions[7], y_top - header_top_h, size=12, bold=True)
    draw_centered_wrapped_text(c, "Total (Kg)", x_positions[7], y_top - header_h, x_positions[8], y_top - header_top_h, size=12, bold=True)

    y = y_top - header_h
    for values, row_height in zip(row_values, row_heights):
        row_bottom = y - row_height

        for col_index, value in enumerate(values):
            draw_centered_wrapped_text(c, value, x_positions[col_index], row_bottom, x_positions[col_index + 1], y, size=12)

        y = row_bottom

    return y


def draw_structure_table(c, x, y_top, rows, start_no=1):
    col_widths = [42, 240, 160, 270, 115, 120, 103]
    header_h = 35
    display_rows = rows if rows else [None]
    x_positions = get_x_positions(x, col_widths)

    row_values = [
        get_structure_row_values(row, index)
        for index, row in enumerate(display_rows, start=start_no)
    ]

    row_heights = [
        calculate_row_height(values, col_widths, min_height=25, font=TABLE_FONT, size=12)
        for values in row_values
    ]

    table_bottom = y_top - header_h - sum(row_heights)
    row_heights_with_header = [header_h] + row_heights

    draw_table_grid(c, x, y_top, col_widths, row_heights_with_header)

    y_bottom = y_top - header_h
    headers = [
        "No.",
        "Name of equipment and machinery",
        "Location",
        "Materials (classification in appendix 1)",
        "Parts were used",
        "Approximate\nquantity",
        "Remarks",
    ]

    for col_index, label in enumerate(headers):
        draw_centered_wrapped_text(c, label, x_positions[col_index], y_bottom, x_positions[col_index + 1], y_top, size=12, bold=True)

    y = y_bottom
    for values, row_height in zip(row_values, row_heights):
        row_bottom = y - row_height

        for col_index, value in enumerate(values):
            draw_centered_wrapped_text(c, value, x_positions[col_index], row_bottom, x_positions[col_index + 1], y, size=12)

        y = row_bottom

    return y

def draw_paginated_table(
    c,
    page_width,
    page_height,
    y,
    section_title,
    rows,
    draw_table_func,
    header_h,
    row_h,
    vessel_name,
    company_name,
    first_section=True
):
    left = CONTENT_LEFT
    row_index = 0
    display_rows = rows if rows else [None]

    while row_index < len(display_rows):
        section_space = 18 if first_section else 0
        available_height = y - CONTENT_BOTTOM_Y - section_space - SECTION_LABEL_GAP - header_h

        max_rows = int(available_height // row_h)

        if max_rows <= 0:
            c.showPage()
            draw_common_header_footer(
                c,
                vessel_name=vessel_name,
                company_name=company_name,
                page_no_text="Page | "
            )
            y = page_height - NEXT_PAGE_CONTENT_TOP_OFFSET
            first_section = False
            continue

        chunk = display_rows[row_index:row_index + max_rows]

        if first_section:
            draw_section_label(c, left, y, section_title)
            table_y = y - SECTION_LABEL_GAP
        else:
            table_y = y

        y = draw_table_func(
            c,
            left,
            table_y,
            chunk,
            start_no=row_index + 1
        ) - SECTION_BOTTOM_GAP

        row_index += len(chunk)
        first_section = False

        if row_index < len(display_rows):
            c.showPage()
            draw_common_header_footer(
                c,
                vessel_name=vessel_name,
                company_name=company_name,
                page_no_text="Page | "
            )
            y = page_height - NEXT_PAGE_CONTENT_TOP_OFFSET

    return y

def make_hazardous_materials_a3_page(data):
    packet = BytesIO()

    page_width, page_height = landscape(A3)
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    job = data.get("job") or {}
    vessel = data.get("vessel") or {}
    report = data.get("reportInfo") or {}

    vessel_name = vessel.get("vesselName") or job.get("vesselName")
    company_name = report.get("companyName")

    draw_common_header_footer(
            c,
            vessel_name=vessel_name,
            company_name=company_name,
            page_no_text="Page | "
        )
    draw_title(c, page_width, page_height)

    y = page_height - FIRST_PAGE_CONTENT_TOP_OFFSET

    paint_rows = get_rows(data, "paintAndCoatingSystems", "paints")
    equipment_rows = get_rows(data, "equipmentAndMachinery", "equipment")
    structure_rows = get_rows(data, "structureAndHull", "structures")


    y = draw_paginated_table(
        c,
        page_width,
        page_height,
        y,
        "I-1 Paints and coating systems containing materials listed in Table A and Table B of appendix 1 of the guidelines",
        paint_rows,
        draw_paint_table,
        header_h=50,
        row_h=38,
        vessel_name=vessel_name,
        company_name=company_name,
        first_section=True
    )

    y = draw_paginated_table(
        c,
        page_width,
        page_height,
        y,
        "I-2 Equipment and Machinery containing materials listed in Table A and B of appendix of the guidelines",
        equipment_rows,
        draw_equipment_table,
        header_h=70,
        row_h=38,
        vessel_name=vessel_name,
        company_name=company_name,
        first_section=True
    )

    y = draw_paginated_table(
        c,
        page_width,
        page_height,
        y,
        "I-3 Structure and hull containing materials listed in Table A and Table B of appendix 1 of the guidelines.",
        structure_rows,
        draw_structure_table,
        header_h=50,
        row_h=38,
        vessel_name=vessel_name,
        company_name=company_name,
        first_section=True
    )

    if y < CONTENT_BOTTOM_Y + 20:
        c.showPage()
        draw_common_header_footer(
            c,
            vessel_name=vessel_name,
            company_name=company_name,
            page_no_text="Page | "
        )
        y = page_height - NEXT_PAGE_CONTENT_TOP_OFFSET

    c.setFont(TABLE_FONT_BOLD, 12)
    c.drawString(CONTENT_LEFT, y, "Accurate quantification has not been practicably possible")

    c.save()
    packet.seek(0)

    reader = PdfReader(packet)
    pages = list(reader.pages)

    pages.extend(make_i2_hazard_layout_pages(
        data,
        vessel_name=vessel_name,
        company_name=company_name
    ))

    return pages