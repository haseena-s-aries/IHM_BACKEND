from io import BytesIO

from pypdf import PdfReader
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
    return list(reader.pages)