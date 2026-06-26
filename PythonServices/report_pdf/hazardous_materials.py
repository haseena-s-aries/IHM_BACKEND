from io import BytesIO
import sys
from pathlib import Path
from urllib.parse import urlparse

from pypdf import PdfReader
from PIL import Image, ImageDraw, ImageFont
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


def dedupe_latest_markers(markers):
    latest = {}

    for marker in markers:
        label = marker_label(marker).upper()
        image_path = normalize_web_path(marker_image_path(marker))
        if not label or not image_path:
            continue

        key = (label, image_path)
        marker_id = int(marker.get("id") or marker.get("Id") or 0)

        existing = latest.get(key)
        existing_id = int(existing.get("id") or existing.get("Id") or 0) if existing else -1

        if existing is None or marker_id >= existing_id:
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


def draw_hazard_marked_layout_image(source_path, marker_items, output_path):
    base = Image.open(source_path).convert("RGB")
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

        x = outer_pad + (float(marker.get("xPercent") or marker.get("XPercent") or 0) / 100) * image_w
        y = outer_pad + (float(marker.get("yPercent") or marker.get("YPercent") or 0) / 100) * image_h


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

        # Keep callouts on left/right so connector lines stay mostly horizontal
        # and do not cut vertically through rooms/cells.
        dist_left = x - layout_x
        dist_right = layout_right - x

        if dist_left <= dist_right:
            side = "left"
            box_x = layout_x - line_gap - label_w
            box_y = y - label_h / 2
            line_end = (box_x + label_w, box_y + label_h / 2)
        else:
            side = "right"
            box_x = layout_right + line_gap
            box_y = y - label_h / 2
            line_end = (box_x, box_y + label_h / 2)

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

        if side == "left":
            line_end = (box_x + label_w, box_y + label_h / 2)
        else:
            line_end = (box_x, box_y + label_h / 2)

        box = (box_x, box_y, box_x + label_w, box_y + label_h)
        used_boxes.append(box)

        placements.append({
            "x": x,
            "y": y,
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
        label = marker_label(marker).upper()
        if label:
            markers_by_label.setdefault(label, []).append(marker)

    grouped_by_image = {}

    for row in equipment_rows:
        sample_no = equipment_sample_no(row).upper()
        if not sample_no:
            continue

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

    generated = []
    print(f"I-2 hazard layout image groups: {len(grouped_by_image)}", file=sys.stderr)
    output_dir = Path.cwd() / "wwwroot" / "reports" / "ihm" / str((data.get("job") or {}).get("id") or "0") / "i2-hazard-layouts"

    for index, group in enumerate(grouped_by_image.values(), start=1):
        source_path = resolve_report_image_path(group["source"])
        if not source_path or not source_path.exists():
            continue

        output_path = output_dir / f"i2-hazard-layout-{index}.png"
        generated.append(draw_hazard_marked_layout_image(
            source_path,
            group["items"],
            output_path
        ))

    return generated


def make_i2_hazard_layout_pages(data, vessel_name, company_name):
    images = build_i2_hazard_layout_images(data)
    pages = []

    for image_path in images:
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

        usable_x = 45
        usable_y = 75
        usable_w = HAZARD_LAYOUT_PAGE_WIDTH - 90
        usable_h = HAZARD_LAYOUT_PAGE_HEIGHT - 145

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