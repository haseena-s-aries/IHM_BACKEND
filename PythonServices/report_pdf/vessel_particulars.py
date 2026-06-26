from io import BytesIO

from pypdf import PdfReader
from reportlab.lib import colors
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfgen import canvas

from report_pdf.helpers import (
    FONT_TIMES,
    txt,
    format_date,
    format_number,
    split_text_to_lines,
    draw_common_header_footer,calculate_row_height,wrap_text_to_lines,
    calculate_row_height,format_build_date
)


def erase_vessel_particular_area(c):
    c.setFillColor(colors.white)
    c.rect(60, 60, 500, 700, stroke=0, fill=1)
    c.setFillColor(colors.black)


def draw_vessel_cell(c, text, x1, y_bottom, x2, y_top, align="left"):
    value = txt(text).upper()
    if value.endswith(" M"):
        value = value[:-2] + " m"
    if not value:
        return

    font = FONT_TIMES
    size = 12
    cell_w = x2 - x1
    cell_h = y_top - y_bottom

    lines = wrap_text_to_lines(value, font, size, cell_w - 8)

    leading = size + 2
    total_h = len(lines) * leading
    start_y = y_bottom + (cell_h - total_h) / 2 + (len(lines) - 1) * leading

    c.setFont(font, size)
    c.setFillColor(colors.black)

    for i, line in enumerate(lines):
        line_w = pdfmetrics.stringWidth(line, font, size)

        if align == "right":
            x = x2 - line_w - 5
        else:
            x = x1 + 5

        y = start_y - (i * leading)
        c.drawString(x, y, line)


def draw_vessel_row_border(c, y_top, row_h):
    y_bottom = y_top - row_h

    c.setStrokeColor(colors.black)
    c.setLineWidth(0.5)

    c.rect(70, y_bottom, 470, row_h, stroke=1, fill=0)
    c.line(320, y_bottom, 320, y_top)


def draw_vessel_particular_table(c, rows, start_index, y_top=760):
    
    row_index = start_index

    while row_index < len(rows):
        
        label, value = rows[row_index]
        row_h = calculate_row_height(
            [label, value],
            [250, 220],
            min_height=40,
            font=FONT_TIMES,
            size=12
        )
        if y_top - row_h < 80:
            break
        draw_vessel_row_border(c, y_top, row_h)

        y_bottom = y_top - row_h

        draw_vessel_cell(c, label, 70, y_bottom, 320, y_top, align="left")
        draw_vessel_cell(c, value, 320, y_bottom, 540, y_top, align="right")

        row_index += 1
        y_top -= row_h

    return row_index


def draw_vessel_particulars_page(c, page_width, page_height, data, start_row_index=0, first_template_page=False):
    job = data.get("job") or {}
    vessel = data.get("vessel") or {}
    report = data.get("reportInfo") or {}

    rows = build_vessel_particular_rows(data)

    if first_template_page:
        erase_vessel_particular_area(c)
    else:
        vessel_name = vessel.get("vesselName") or job.get("vesselName")
        company_name = report.get("companyName")
        draw_common_header_footer(
            c,
            vessel_name=vessel_name,
            company_name=company_name,
            page_no_text="Page | "
        )

    return draw_vessel_particular_table(
        c,
        rows,
        start_row_index,
        y_top=760
    )


def make_blank_page_with_vessel_rows(page_width, page_height, data, start_row_index):
    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    next_index = draw_vessel_particulars_page(
        c,
        page_width,
        page_height,
        data,
        start_row_index=start_row_index,
        first_template_page=False
    )

    c.save()
    packet.seek(0)

    reader = PdfReader(packet)
    return reader.pages[0], next_index


def build_vessel_particular_rows(data):
    vessel = data.get("vessel") or {}
    job = data.get("job") or {}

    rows = [
        ("NAME OF THE VESSEL", vessel.get("vesselName") or job.get("vesselName")),
        ("VESSEL TYPE", vessel.get("vesselType")),
        ("CLASSIFICATION SOCIETY", vessel.get("classSociety")),
        ("CALL SIGN", vessel.get("callSign")),
        ("IMO NUMBER", vessel.get("imoNumber") or job.get("imoNumber")),
        ("OFFICIAL NUMBER", vessel.get("officialNumber")),
        ("BUILDER", vessel.get("builder")),
        ("DATE OF BUILD", format_build_date(vessel.get("dateOfBuild"))),
        ("PORT OF REGISTRY", vessel.get("portOfRegistry")),
        ("FLAG", vessel.get("flag")),
        ("L.O.A.", format_number(vessel.get("lengthOverall"), "m")),
        ("L.B.P.", format_number(vessel.get("lengthBetweenPerpendiculars"), "m")),
        ("BREADTH (MLD)", format_number(vessel.get("breadth"), "m")),
        ("DEPTH (MLD)", format_number(vessel.get("depth"), "m")),
        ("DRAUGHT (MLD)", format_number(vessel.get("draughtMoulded"), "m")),
        ("GRT", format_number(vessel.get("grossTonnage"))),
        ("NRT", format_number(vessel.get("netTonnage"))),
    ]

    return [(label, value) for label, value in rows if txt(value)]


def draw_vessel_particular_table_page_count_index(total_rows):
    row_h = 40
    next_index = 0
    y_top = 760

    while next_index < total_rows:
        if y_top - row_h < 80:
            break

        next_index += 1
        y_top -= row_h

    return next_index