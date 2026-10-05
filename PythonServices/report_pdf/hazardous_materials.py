from io import BytesIO
import sys
import json
import hashlib
import time
from pathlib import Path
from urllib.parse import urlparse
import re
from pypdf import PdfReader
from PIL import Image, ImageDraw, ImageFont, ImageOps

# Uploaded vessel drawings can legitimately be very large. The generator
# applies its own maximum working size below, so Pillow's defensive pixel
# limit must not reject a trusted uploaded layout before it can be resized.
Image.MAX_IMAGE_PIXELS = None
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
def _stable_json(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), default=str)

def _file_fingerprint(path):
    path = Path(path)
    stat = path.stat()
    return {
        "path": str(path.resolve()),
        "mtimeNs": stat.st_mtime_ns,
        "size": stat.st_size,
    }

def _cache_key(source_path, items, extra=None):
    payload = {
        "source": _file_fingerprint(source_path),
        "items": items,
        "extra": extra or {},
    }
    return hashlib.sha256(_stable_json(payload).encode("utf-8")).hexdigest()

def _layout_cache_dir(data):
    job_id = ((data.get("job") or {}).get("id") or "0")
    path = Path.cwd() / "wwwroot" / "reports" / "ihm" / str(job_id) / "layout-image-cache"
    path.mkdir(parents=True, exist_ok=True)
    return path

def _cache_manifest_path(cache_dir, cache_key):
    return cache_dir / f"{cache_key}.json"

def _cache_image_path(cache_dir, cache_key):
    return cache_dir / f"{cache_key}.png"

def _try_cached_layout(cache_dir, cache_key):
    manifest_path = _cache_manifest_path(cache_dir, cache_key)
    image_path = _cache_image_path(cache_dir, cache_key)

    if manifest_path.exists() and image_path.exists():
        print(f"CACHE HIT layout={image_path}", file=sys.stderr)
        return image_path

    print(f"CACHE MISS key={cache_key}", file=sys.stderr)
    return None

def _write_cache_manifest(cache_dir, cache_key, payload):
    _cache_manifest_path(cache_dir, cache_key).write_text(
        _stable_json(payload),
        encoding="utf-8"
    )

def make_hazard_pdf_ready_image(image_path, target_width_pt, target_height_pt, label):
    image_path = Path(image_path)

    target_w_px = max(1, int(float(target_width_pt) / 72 * 300))
    target_h_px = max(1, int(float(target_height_pt) / 72 * 300))

    ready_path = image_path.parent / f"{image_path.stem}-pdf-ready.tmp.png"

    if ready_path.exists() and ready_path.stat().st_mtime >= image_path.stat().st_mtime:
        return ready_path

    with Image.open(image_path) as img:
        img = ImageOps.exif_transpose(img).convert("RGB")

        if img.width < target_w_px or img.height < target_h_px:
            scale = min(target_w_px / img.width, target_h_px / img.height)
            new_size = (max(1, int(img.width * scale)), max(1, int(img.height * scale)))
            img = img.resize(new_size, Image.Resampling.LANCZOS)
        else:
            img.thumbnail((target_w_px, target_h_px), Image.Resampling.LANCZOS)

        img.save(ready_path, "PNG", dpi=(450, 450))

    return ready_path

def detect_existing_sample_text_markings(image_path, expected_labels=None):
    expected = {
        normalize_sample_label(label)
        for label in (expected_labels or [])
        if normalize_sample_label(label)
    }

    ocr_started = time.perf_counter()
    print(f"START OCR image={image_path}", file=sys.stderr)
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
    image_path = Path(image_path)
    expected = sorted([
        normalize_sample_label(x)
        for x in (expected_labels or [])
        if normalize_sample_label(x)
    ])

    stat = image_path.stat()
    cache_root = Path.cwd() / "wwwroot" / "reports" / "ihm" / "ocr-cache"
    cache_root.mkdir(parents=True, exist_ok=True)

    cache_payload = {
        "path": str(image_path.resolve()),
        "mtimeNs": stat.st_mtime_ns,
        "size": stat.st_size,
        "expected": expected,
    }

    cache_key = hashlib.sha256(
        json.dumps(cache_payload, sort_keys=True).encode("utf-8")
    ).hexdigest()

    cache_file = cache_root / f"{cache_key}.json"

    if cache_file.exists():
        print(f"CACHE HIT OCR {image_path}", file=sys.stderr)
        return json.loads(cache_file.read_text(encoding="utf-8"))

    print(f"CACHE MISS OCR {image_path}", file=sys.stderr)

    by_label = {}

    for item in detect_existing_blue_sample_markings(image_path):
        label = normalize_sample_label(item.get("label"))
        if label:
            by_label[label] = item

    for item in detect_existing_sample_text_markings(image_path, expected_labels):
        label = normalize_sample_label(item.get("label"))
        if label:
            by_label[label] = item

    result = list(by_label.values())

    cache_file.write_text(
        json.dumps(result, ensure_ascii=False),
        encoding="utf-8"
    )

    return result


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
    ocr_started = time.perf_counter()
    print(f"START OCR blue-sample image={image_path}", file=sys.stderr)
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
            print(
                f"END OCR image={image_path} elapsed_ms={int((time.perf_counter() - ocr_started) * 1000)}",
                file=sys.stderr
            )
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
        # Break unusually long words instead of dropping or truncating them.
        if draw.textbbox((0, 0), word, font=font)[2] - draw.textbbox((0, 0), word, font=font)[0] > max_width:
            chunk = ""
            for char in word:
                candidate = chunk + char
                if chunk and draw.textbbox((0, 0), candidate, font=font)[2] - draw.textbbox((0, 0), candidate, font=font)[0] > max_width:
                    lines.append(chunk)
                    chunk = char
                else:
                    chunk = candidate
            current = chunk
        else:
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

    # Strict marker-blue mask. Avoid room fills, black text, ship outline, and pale grid.
    strong_blue_mask = (
        (blue > 140) &
        (red < 110) &
        (green < 135) &
        ((blue - red) > 55) &
        ((blue - green) > 35)
    )

    # Catch only anti-aliased edge pixels that are directly touching strong marker blue.
    edge_blue_mask = (
        (blue > 115) &
        (red < 150) &
        (green < 165) &
        ((blue - red) > 35) &
        ((blue - green) > 22)
    )

    strong_blue_mask_u8 = strong_blue_mask.astype(np.uint8) * 255
    edge_near_strong = cv2.dilate(
        strong_blue_mask_u8,
        cv2.getStructuringElement(cv2.MORPH_RECT, (3, 3)),
        iterations=1
    ) > 0

    blue_mask = (
        strong_blue_mask |
        (edge_blue_mask & edge_near_strong)
    ).astype(np.uint8) * 255

    # Use connected components only for protecting OCR-current markers.
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

        # OCR coordinate is label/text location. Protect only nearby blue component.
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
        if component_area > image_w * image_h * 0.03:
            continue

        component_mask = (labels == component_id).astype(np.uint8) * 255
        protected_mask = cv2.bitwise_or(
            protected_mask,
            cv2.bitwise_and(blue_mask, component_mask)
        )

    erase_mask = cv2.bitwise_and(blue_mask, cv2.bitwise_not(protected_mask))

    # Small expansion removes anti-aliasing but avoids deleting grid/room boundaries.
    erase_mask = cv2.dilate(
        erase_mask,
        cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3)),
        iterations=1
    )

    # Smooth mask edges before inpaint to avoid rectangular/white artifacts.
    erase_mask = cv2.GaussianBlur(erase_mask, (3, 3), 0)
    erase_mask = (erase_mask > 20).astype(np.uint8) * 255

    if cv2.countNonZero(erase_mask) == 0:
        return Image.fromarray(arr.astype(np.uint8))

    # TELEA generally blends thin blue lines better than white fill.
    cleaned = cv2.inpaint(
        arr.astype(np.uint8),
        erase_mask,
        3,
        cv2.INPAINT_TELEA
    )

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
    # Source layout uploads are treated as clean source drawings. Do not run
    # OCR or erase existing blue markings here; OCR was adding a large delay
    # even when the uploaded image contained no markings. The generator now
    # only adds the current database markers to the source image.
    image_w, image_h = base.size
    max_working_side = 7000
    if max(image_w, image_h) > max_working_side:
        scale = max_working_side / float(max(image_w, image_h))
        base = base.resize(
            (max(1, int(round(image_w * scale))), max(1, int(round(image_h * scale)))),
            Image.Resampling.LANCZOS,
        )
        image_w, image_h = base.size

    # Reserve enough white margin for the wider callout columns.
    estimated_column_width = max(180, min(520, int(round(image_w * 0.16))))
    outer_pad = max(220, min(620, estimated_column_width + 40))
    canvas_img = Image.new(
        "RGB",
        (image_w + outer_pad * 2, image_h + outer_pad * 2),
        (255, 255, 255)
    )
    canvas_img.paste(base, (outer_pad, outer_pad))

    draw = ImageDraw.Draw(canvas_img)

    gray = cv2.cvtColor(np.array(base), cv2.COLOR_RGB2GRAY)
    content_mask = (gray < 235).astype(np.uint8)
    content_integral = cv2.integral(content_mask)

    def clean_label_box(box):
        x1, y1, x2, y2 = [int(round(value)) for value in box]
        x1 -= outer_pad
        x2 -= outer_pad
        y1 -= outer_pad
        y2 -= outer_pad
        x1, y1 = max(0, x1 - 8), max(0, y1 - 8)
        x2, y2 = min(image_w, x2 + 8), min(image_h, y2 + 8)
        if x2 <= x1 or y2 <= y1:
            return False
        occupied = (
            content_integral[y2, x2]
            - content_integral[y1, x2]
            - content_integral[y2, x1]
            + content_integral[y1, x1]
        )
        return occupied / float((x2 - x1) * (y2 - y1)) <= 0.04

    # Responsive callout sizing. Keep small source images compact and use a
    # lighter font so the label column remains readable without dominating it.
    min_dimension = min(image_w, image_h)
    # Hazardous layouts are now fitted wider on the page. Keep the label text
    # readable at that scale and let the existing wrapped-text sizing increase
    # the cell height when required.
    # PDF-derived layouts can have a much larger raster canvas than their
    # final report size. A 24-42px range becomes too small after page fitting,
    # so use a larger readable range; wrapped text sizing below expands each
    # cell to match the selected font.
    dynamic_font_size = max(32, min(56, int(round(min_dimension / 75))))
    # Prefer a wider, compact label column so descriptions wrap into readable
    # lines instead of producing tall one-word-per-line cells.
    column_width = max(180, min(520, int(round(image_w * 0.16))))
    column_height = max(34, min(58, int(round(min_dimension / 62))))
    try:
        font = ImageFont.truetype("arial.ttf", dynamic_font_size)
    except OSError:
        try:
            font = ImageFont.truetype("arial.ttf", dynamic_font_size)
        except OSError:
            font = ImageFont.load_default()

    blue = (0, 45, 210)
    white = (255, 255, 255)

    placements = []
    used_boxes = []
    existing_segments = []
    reserved_bend_points = set()
    label_side_counts = {
        "top": 0,
        "bottom": 0,
        "left": 0,
        "right": 0,
    }
    target_boxes = []
    label_h_base = column_height
    # Keep stacked hazardous callout cells readable without creating large
    # empty gaps. The same spacing is used for left-column overflow on right.
    gap = 16
    line_gap = 28
    margin = 8
    collision_pad = 8
    route_gap = 10
    outside_gap = line_gap

    layout_x = outer_pad
    layout_y = outer_pad
    layout_right = outer_pad + image_w
    layout_bottom = outer_pad + image_h

    def detect_compartment_rects(image):
        cv_img = cv2.cvtColor(np.array(image.convert("RGB")), cv2.COLOR_RGB2BGR)
        hsv = cv2.cvtColor(cv_img, cv2.COLOR_BGR2HSV)
        saturation = hsv[:, :, 1]
        value = hsv[:, :, 2]
        mask = cv2.inRange(saturation, 35, 255)
        mask = cv2.bitwise_and(mask, cv2.inRange(value, 80, 255))
        kernel = np.ones((5, 5), np.uint8)
        mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel, iterations=2)
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        rects = []

        for contour in contours:
            x, y, w, h = cv2.boundingRect(contour)
            if w < 35 or h < 22:
                continue
            if w > image_w * 0.75 or h > image_h * 0.75:
                continue
            rects.append((outer_pad + x, outer_pad + y, outer_pad + x + w, outer_pad + y + h))

        return rects

    obstacle_rects = detect_compartment_rects(base)

    label_index = 0
    left_column_y = margin
    right_column_y = margin
    for item in marker_items:
        marker = item["marker"]
        notes = str(marker.get("notes") or marker.get("Notes") or "").strip().lower()
        marker_source = str(marker.get("source") or marker.get("Source") or "").strip().lower()
        is_existing_label = (
            "ocr-existing-label" in notes
            or "manual-for-existing-image" in notes
            or marker_source == "existing-image"
        )
        if is_existing_label:
            continue

        x = outer_pad + percent_to_unit(marker.get("xPercent") or marker.get("XPercent")) * image_w
        y = outer_pad + percent_to_unit(marker.get("yPercent") or marker.get("YPercent")) * image_h
        target_boxes.append((x - 9, y - 9, x + 9, y + 9))

    def boxes_overlap(a, b, pad=gap):
        ax1, ay1, ax2, ay2 = a
        bx1, by1, bx2, by2 = b
        return not (
            ax2 + pad < bx1 or
            ax1 - pad > bx2 or
            ay2 + pad < by1 or
            ay1 - pad > by2
        )

    def segment_hits_rect(segment, rect):
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

        return not (
            max(x1, x2) + collision_pad < rx1 or
            min(x1, x2) - collision_pad > rx2 or
            max(y1, y2) + collision_pad < ry1 or
            min(y1, y2) - collision_pad > ry2
        )

    def segments_touch_or_overlap(a, b, tolerance=14):
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
        a_vertical = abs(ax1 - ax2) < 0.1
        b_vertical = abs(bx1 - bx2) < 0.1
        a_horizontal = abs(ay1 - ay2) < 0.1
        b_horizontal = abs(by1 - by2) < 0.1

        if a_vertical and b_horizontal:
            return min(bx1, bx2) <= ax1 <= max(bx1, bx2) and min(ay1, ay2) <= by1 <= max(ay1, ay2)
        if a_horizontal and b_vertical:
            return min(ax1, ax2) <= bx1 <= max(ax1, ax2) and min(by1, by2) <= ay1 <= max(by1, by2)

        return False

    def path_segments(path):
        return list(zip(path, path[1:]))

    def rect_contains_point(rect, point):
        x1, y1, x2, y2 = rect
        x, y = point
        return x1 <= x <= x2 and y1 <= y <= y2

    def bend_key(point):
        return (round(point[0] / 4), round(point[1] / 4))

    def marker_side(x, y):
        half_w = max(180, min(image_w * 0.22, 900))
        half_h = max(140, min(image_h * 0.22, 700))
        local_left = max(layout_x, x - half_w)
        local_right = min(layout_right, x + half_w)
        local_top = max(layout_y, y - half_h)
        local_bottom = min(layout_bottom, y + half_h)
        return min(
            [
                ("left", x - local_left),
                ("right", local_right - x),
                ("top", y - local_top),
                ("bottom", local_bottom - y),
            ],
            key=lambda item: item[1],
        )[0]

    def label_anchor(box, side):
        x1, y1, x2, y2 = box
        if side == "top":
            return ((x1 + x2) / 2, y2)
        if side == "bottom":
            return ((x1 + x2) / 2, y1)
        if side == "left":
            return (x2, (y1 + y2) / 2)
        return (x1, (y1 + y2) / 2)

    def target_point(x, y, side):
        if side == "top":
            return (x, max(layout_y, y - 1))
        if side == "bottom":
            return (x, min(layout_bottom, y + 1))
        if side == "left":
            return (max(layout_x, x - 1), y)
        return (min(layout_right, x + 1), y)

    def path_has_collision(path, target_rect):
        target = path[-1]
        for segment in path_segments(path):
            for box in used_boxes:
                if segment_hits_rect(segment, box):
                    return True
            for other_target in target_boxes:
                if other_target != target_rect and segment_hits_rect(segment, other_target):
                    return True
            for obstacle_rect in obstacle_rects:
                if rect_contains_point(obstacle_rect, target):
                    continue
                if segment_hits_rect(segment, obstacle_rect):
                    return True
            for existing in existing_segments:
                if segments_touch_or_overlap(segment, existing):
                    return True
                if segments_cross(segment, existing):
                    return True

        return any(bend_key(point) in reserved_bend_points for point in path[1:-1])

    def path_length(path):
        total = 0
        for p1, p2 in path_segments(path):
            total += abs(p1[0] - p2[0]) + abs(p1[1] - p2[1])
        return total + (max(0, len(path) - 2) * 80)

    def route_path(anchor, target, side, target_rect, route_index=0):
        if side in ("left", "right"):
            direction = 1 if side == "left" else -1
            short_exit = anchor[0] + direction * (30 + route_index * 28)
            return [anchor, (short_exit, anchor[1]), target]
        groups = []
        direct = []
        if side in ("top", "bottom") and abs(anchor[0] - target[0]) < 1:
            direct.append([anchor, target])
        elif side in ("left", "right") and abs(anchor[1] - target[1]) < 1:
            direct.append([anchor, target])
        groups.append(direct)

        if side in ("top", "bottom"):
            groups.append([
                [anchor, (anchor[0], target[1]), target],
                [anchor, (target[0], anchor[1]), target],
            ])

            outside_step = -route_gap if side == "top" else route_gap
            outside_y = layout_y - route_gap if side == "top" else layout_bottom + route_gap
            lane_candidates = []
            for offset in (12, -12, 22, -22, 34, -34, 46, -46):
                lane_x = target[0] + offset
                lane_x = max(layout_x - outside_gap, min(lane_x, layout_right + outside_gap))
                lane_candidates.append([
                    anchor,
                    (anchor[0], anchor[1] + outside_step),
                    (lane_x, anchor[1] + outside_step),
                    (lane_x, target[1]),
                    target,
                ])
                lane_candidates.append([
                    anchor,
                    (anchor[0], outside_y),
                    (lane_x, outside_y),
                    (lane_x, target[1]),
                    target,
                ])
            groups.append(lane_candidates)
        else:
            groups.append([
                [anchor, (target[0], anchor[1]), target],
                [anchor, (anchor[0], target[1]), target],
            ])

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
            groups.append(lane_candidates)

        for candidates in groups:
            safe = [path for path in candidates if not path_has_collision(path, target_rect)]
            if safe:
                return min(safe, key=path_length)

        return min(groups[-1], key=path_length)

    for item in marker_items:
        marker = item["marker"]
        label = item["label"]

        notes = str(marker.get("notes") or marker.get("Notes") or "").strip().lower()
        marker_source = str(marker.get("source") or marker.get("Source") or "").strip().lower()
        is_existing_label = (
            "ocr-existing-label" in notes
            or "manual-for-existing-image" in notes
            or marker_source == "existing-image"
        )

        x = outer_pad + percent_to_unit(marker.get("xPercent") or marker.get("XPercent")) * image_w
        y = outer_pad + percent_to_unit(marker.get("yPercent") or marker.get("YPercent")) * image_h

        if is_existing_label:
            max_label_w = 360
            lines = wrap_pil_text(draw, label, font, max_label_w - 28)
            line_h = max(dynamic_font_size + 4, int(round(dynamic_font_size * 1.25)))

            label_w = max(
                90,
                min(
                    max_label_w,
                    max(draw.textbbox((0, 0), line, font=font)[2] for line in lines) + 28
                )
            )
            label_h = max(label_h_base, len(lines) * line_h + 18)

            box_x = x - label_w / 2
            box_y = y - label_h / 2

            box_x = max(margin, min(box_x, canvas_img.size[0] - label_w - margin))
            box_y = max(margin, min(box_y, canvas_img.size[1] - label_h - margin))

            # OCR x/y is the existing label text position. Replace text only.
            # Do not redraw dot, leader line, or full label border.
            
            text_clear_pad_x = max(label_w / 2, 55)
            text_clear_pad_y = max(label_h / 2, 30)

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

            total_text_h = len(lines) * line_h
            text_y = clear_y1 + max(8, ((clear_y2 - clear_y1) - total_text_h) / 2)

            for line in lines:
                line_box = draw.textbbox((0, 0), line, font=font)
                line_w = line_box[2] - line_box[0]
                text_x = clear_x1 + max(0, ((clear_x2 - clear_x1) - line_w) / 2)
                draw.text((text_x, text_y), line, fill=blue, font=font)
                text_y += line_h

            used_boxes.append((clear_x1, clear_y1, clear_x2, clear_y2))
            continue


        # One fixed width/height per column keeps all stacked cell borders
        # perfectly aligned. Long labels wrap inside the same cell.
        label_w = column_width
        line_h = max(dynamic_font_size + 6, int(round(dynamic_font_size * 1.25)))
        lines = wrap_pil_text(draw, label, font, label_w - 16)
        total_text_height = len(lines) * line_h
        label_h = max(label_h_base, total_text_height + 16)

        def make_candidate_box(candidate_side, lane=0):
            side_index = label_side_counts.get(candidate_side, 0)
            spread_steps = [0, -52, 52, -96, 96, -140, 140, -184, 184]
            spread = spread_steps[side_index % len(spread_steps)]
            half_w = max(180, min(image_w * 0.22, 900))
            half_h = max(140, min(image_h * 0.22, 700))
            local_left = max(layout_x, x - half_w)
            local_right = min(layout_right, x + half_w)
            local_top = max(layout_y, y - half_h)
            local_bottom = min(layout_bottom, y + half_h)

            if candidate_side == "top":
                return (
                    x - label_w / 2 + spread,
                    local_top - line_gap - label_h - (lane * (label_h + gap))
                )

            if candidate_side == "bottom":
                return (
                    x - label_w / 2 + spread,
                    local_bottom + line_gap + (lane * (label_h + gap))
                )

            if candidate_side == "left":
                return (
                    local_left - line_gap - label_w - (lane * (label_w + gap)),
                    y - label_h / 2 + spread
                )

            return (
                local_right + line_gap + (lane * (label_w + gap)),
                y - label_h / 2 + spread
            )

        # Fill the left column by cumulative height, then overflow to right.
        if left_column_y + label_h <= canvas_img.size[1] - margin:
            side = "left"
            box_y = left_column_y
            left_column_y += label_h + gap
        else:
            side = "right"
            box_y = right_column_y
            right_column_y += label_h + gap
        box_x = (layout_x - outside_gap - label_w) if side == "left" else (layout_right + outside_gap)

        box_x = max(margin, min(box_x, canvas_img.size[0] - label_w - margin))
        box_y = max(margin, min(box_y, canvas_img.size[1] - label_h - margin))

        box = (box_x, box_y, box_x + label_w, box_y + label_h)
        anchor = label_anchor(box, side)
        target = (x, y)
        target_rect = (x - 9, y - 9, x + 9, y + 9)
        path = route_path(anchor, target, side, target_rect, label_index)
        label_index += 1
        label_side_counts[side] = label_side_counts.get(side, 0) + 1
        used_boxes.append(box)
        existing_segments.extend(path_segments(path))
        for bend in path[1:-1]:
            reserved_bend_points.add(bend_key(bend))

        placements.append({
            "x": x,
            "y": y,
            "box": box,
            "path": path,
            "lines": lines,
            "label_h": label_h,
        })

    for item in placements:
        x = item["x"]
        y = item["y"]
        path = item["path"]

        draw.line([coord for point in path for coord in point], fill=blue, width=3)

        dot_radius = 4
        draw.ellipse(
            [x - dot_radius, y - dot_radius, x + dot_radius, y + dot_radius],
            outline=blue,
            fill=white,
            width=2,
        )

    for item in placements:
        x1, y1, x2, y2 = item["box"]
        draw.rectangle([x1, y1, x2, y2], outline=blue, fill=white, width=4)

        line_h = max(dynamic_font_size + 4, int(round(dynamic_font_size * 1.25)))
        total_text_h = len(item["lines"]) * line_h
        text_y = y1 + max(9, ((y2 - y1) - total_text_h) / 2)

        for line in item["lines"]:
            line_box = draw.textbbox((0, 0), line, font=font)
            line_w = line_box[2] - line_box[0]
            text_x = x1 + max(0, ((x2 - x1) - line_w) / 2)
            draw.text((text_x, text_y), line, fill=blue, font=font)
            text_y += line_h

    output_path.parent.mkdir(parents=True, exist_ok=True)
    canvas_img.save(output_path, format="PNG")
    return output_path
def row_area_id(row):
    value = first_value(row, "ihmAreaId", "IhmAreaId", "areaId", "AreaId")
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def layout_area_id(layout):
    value = first_value(layout, "ihmAreaId", "IhmAreaId", "areaId", "AreaId")
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def layout_file_path(layout):
    return first_value(layout, "filePath", "FilePath")


def latest_area_layout_map(data):
    layouts = data.get("areaLayouts") or data.get("AreaLayouts") or []
    result = {}

    for layout in layouts:
        area_id = layout_area_id(layout)
        path = layout_file_path(layout)
        if area_id is None or not path:
            continue

        # Payload is already ordered latest-first per area after backend change,
        # so keep the first layout for each area.
        result.setdefault(area_id, layout)

    return result
def build_i2_hazard_layout_images(data, output_dir=None):
    use_fixed_output_dir = output_dir is not None
    equipment_rows = get_rows(
        data,
        "equipmentAndMachinery",
        "EquipmentAndMachinery",
        "equipment",
        "Equipment"
    )

    markers = dedupe_latest_markers(
        data.get("markers")
        or data.get("Markers")
        or []
    )
    markers_by_label = {}

    for marker in markers:
        label = normalize_sample_label(marker_label(marker))
        if label:
            markers_by_label.setdefault(label, []).append(marker)

    grouped_by_image = {}
    i2_rows_by_sample_no = {}
    area_layout_by_id = latest_area_layout_map(data)
    hazard_area_ids = set()

    for row in equipment_rows:
        area_id = row_area_id(row)
        if area_id is not None:
            hazard_area_ids.add(area_id)

        sample_no = normalize_sample_label(equipment_sample_no(row))
        if not sample_no:
            continue
        i2_rows_by_sample_no[sample_no] = row
        row_markers = markers_by_label.get(sample_no) or []
        label = hazard_marker_label(row)

        if not row_markers:
            area_id = row_area_id(row)
            layout = area_layout_by_id.get(area_id)
            layout_path = layout_file_path(layout) if layout else None
            normalized_layout = normalize_web_path(layout_path)

            if normalized_layout:
                grouped_by_image.setdefault(normalized_layout, {
                    "source": layout_path,
                    "items": []
                })
            continue

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

    missing_sample_nos = [
        sample_no
        for sample_no in missing_sample_nos
        if sample_no not in markers_by_label
    ]

    if not missing_sample_nos:
        print("Hazard OCR skipped: all I-2 samples have marker positions.", file=sys.stderr)

    if missing_sample_nos:
        print(
            "Hazard OCR skipped during generated image creation. "
            "Use Auto Map to create marker positions before generating PDF. "
            "Missing I-2 marker positions: " + ", ".join(missing_sample_nos),
            file=sys.stderr
        )
    if not grouped_by_image:
        for area_id in sorted(hazard_area_ids):
            layout = area_layout_by_id.get(area_id)
            layout_path = layout_file_path(layout) if layout else None
            normalized_layout = normalize_web_path(layout_path)

            if normalized_layout:
                grouped_by_image.setdefault(normalized_layout, {
                    "source": layout_path,
                    "items": []
                })

    if not grouped_by_image:
        common_layout = get_ga_layout_source(data)

        for area_id in sorted(hazard_area_ids):
            layout = area_layout_by_id.get(area_id)
            layout_path = layout_file_path(layout) if layout else None

            if not layout_path:
                layout_path = common_layout

            normalized_layout = normalize_web_path(layout_path)

            if normalized_layout:
                grouped_by_image.setdefault(normalized_layout, {
                    "source": layout_path,
                    "items": []
                })

    generated = []
    print(f"I-2 hazard layout image groups: {len(grouped_by_image)}", file=sys.stderr)
    if output_dir is None:
        job = data.get("job") or data.get("Job") or {}
        job_id = job.get("id") or job.get("Id") or "0"
        output_dir = Path.cwd() / "wwwroot" / "reports" / "ihm" / str(job_id) / "i2-hazard-layouts"
    else:
        output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

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

        output_path = output_dir / f"hazardous-i2-layout-{index}.tmp"
        final_output_path = output_dir / f"hazardous-i2-layout-{index}.png"

        cache_payload = {
            "source": _file_fingerprint(source_path),
            "items": group["items"],
            "bottomDotOffset": bottom_dot_offset,
        }

        if use_fixed_output_dir:
            started = time.perf_counter()
            generated_image = draw_hazard_marked_layout_image(
                source_path,
                group["items"],
                output_path,
                bottom_dot_offset=bottom_dot_offset
            )
            if not output_path.exists() or output_path.stat().st_size <= 0:
                raise RuntimeError(f"Hazardous image output is empty: {output_path}")

            if final_output_path.exists():
                final_output_path.unlink()

            output_path.replace(final_output_path)
            generated_image = final_output_path
            print(
                f"Pre-generated hazardous marked image={generated_image} elapsed_ms={int((time.perf_counter() - started) * 1000)}",
                file=sys.stderr
            )
            generated.append(generated_image)
        else:
            cache_dir = _layout_cache_dir(data)
            cache_key = _cache_key(source_path, group["items"], {"bottomDotOffset": bottom_dot_offset})
            cached_image = _try_cached_layout(cache_dir, cache_key)

            if cached_image is None:
                cache_image = _cache_image_path(cache_dir, cache_key)
                started = time.perf_counter()
                generated_image = draw_hazard_marked_layout_image(
                    source_path,
                    group["items"],
                    cache_image,
                    bottom_dot_offset=bottom_dot_offset
                )
                _write_cache_manifest(cache_dir, cache_key, cache_payload)
                print(
                    f"Image processing generated={generated_image} elapsed_ms={int((time.perf_counter() - started) * 1000)}",
                    file=sys.stderr
                )
                generated.append(generated_image)
            else:
                generated.append(cached_image)

    return generated
def make_hazard_layout_pages_from_images(images, vessel_name, company_name):
    pages = []

    for image_index, image_path in enumerate(images):
        print("HAZARD IMAGE PDF INSERT START", file=sys.stderr)
        image_path = Path(image_path)

        if not image_path.exists() or image_path.stat().st_size <= 0:
            print(f"HAZARD IMAGE PDF INSERT FAILED missing_or_empty={image_path}", file=sys.stderr)
            raise FileNotFoundError(f"Hazardous I-2 image missing or empty: {image_path}")
        with Image.open(image_path) as img:
            img_w, img_h = img.size

        # Hazardous drawing pages use the normal report header/footer. Fit the
        # uploaded drawing inside the available A3 landscape content area so
        # it remains readable without creating an oversized PDF page.
        page_width, page_height = landscape(A3)
        # Keep the report header/footer at the same margins as other pages.
        # The drawing itself may use the wider page area below the header.
        left_margin = 15
        right_margin = 15
        top_content = page_height - 78
        bottom_content = 68
        title_space = 32 if image_index == 0 else 0

        available_w = page_width - left_margin - right_margin
        available_h = top_content - bottom_content - title_space
        # Fill the available drawing rectangle so narrow uploaded layouts are
        # not displayed as a small centered image. The image remains inside
        # the page and does not cover the header/title/footer bands.
        draw_w = available_w
        draw_h = available_h

        packet = BytesIO()
        c = canvas.Canvas(
            packet,
            pagesize=(page_width, page_height)
        )

        draw_common_header_footer(
            c,
            vessel_name=vessel_name,
            company_name=company_name,
            page_no_text=str(image_index + 1)
        )

        if image_index == 0:
            title = "LOCATION DIAGRAM OF HAZARDOUS MATERIALS"
            title_font = TABLE_FONT_BOLD
            title_size = 12
            title_y = top_content - 4
            title_w = pdfmetrics.stringWidth(title, title_font, title_size)

            c.setFont(title_font, title_size)
            c.setFillColor(colors.black)
            c.drawString((page_width - title_w) / 2, title_y, title)
            c.line(
                (page_width - title_w) / 2,
                title_y - 2,
                (page_width + title_w) / 2,
                title_y - 2
            )

        usable_x = left_margin
        usable_y = bottom_content
        usable_w = available_w
        usable_top = top_content - title_space
        usable_h = available_h

        c.drawImage(
            str(image_path),
            usable_x + (usable_w - draw_w) / 2,
            usable_y,
            width=draw_w,
            height=draw_h,
            preserveAspectRatio=False,
            mask="auto"
        )
        print("HAZARD IMAGE PDF INSERT SUCCESS", file=sys.stderr)
        c.save()
        packet.seek(0)
        pages.extend(list(PdfReader(packet).pages))

    return pages

def make_i2_hazard_layout_pages(data, vessel_name, company_name):
    pre_generated_images = (
        data.get("preGeneratedMarkedImages")
        or data.get("PreGeneratedMarkedImages")
        or {}
    )

    pre_generated = (
        pre_generated_images.get("hazardous")
        or pre_generated_images.get("Hazardous")
    )
    print(f"Appendix4 preGeneratedMarkedImages value={pre_generated}", file=sys.stderr)

    all_markers = data.get("markers") or data.get("Markers") or []
    hazardous_markers = [
        marker for marker in all_markers
        if str(marker.get("markerLabel") or marker.get("MarkerLabel") or "").strip()
    ]

    if pre_generated and hazardous_markers:
        manifest_path = resolve_report_image_path(pre_generated)
        if manifest_path and manifest_path.exists():
            try:
                manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
                images = [Path(x) for x in manifest.get("images", []) if Path(x).exists()]
                if images:
                    print(f"Using pre-generated marked image. section=Hazardous count={len(images)}", file=sys.stderr)
                    return make_hazard_layout_pages_from_images(images, vessel_name, company_name)
            except Exception as exc:
                print(f"Marked image missing, using fallback. section=Hazardous error={exc}", file=sys.stderr)
        else:
            print("Marked image missing, using fallback. section=Hazardous", file=sys.stderr)

    print("No hazardous markings found. Skipping hazardous marked image pages.", file=sys.stderr)
    return []

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
