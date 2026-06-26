from io import BytesIO

from pypdf import PdfReader
from reportlab.lib import colors
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfgen import canvas

from report_pdf.helpers import (
    FONT_TIMES,
    draw_common_header_footer,
    txt,
    wrap_text_to_lines,
)


LEFT_X = 70
RIGHT_X = 520
TITLE_Y = 755
START_Y = 720
BOTTOM_Y = 60
LINE_GAP = 28
FONT_SIZE = 12


def _draw_center_title(c, page_width):
    title = "CONTENTS"
    font = "Times-Bold"
    size = 14

    c.setFont(font, size)
    title_width = pdfmetrics.stringWidth(title, font, size)

    x = (page_width - title_width) / 2
    c.drawString(x, TITLE_Y, title)
    c.line(x, TITLE_Y - 2, x + title_width, TITLE_Y - 2)


def _draw_toc_row(c, y, number, title, page_no):
    number = txt(number)
    title = txt(title).upper()
    page_text = txt(page_no)

    c.setFont(FONT_TIMES, FONT_SIZE)
    c.setFillColor(colors.black)

    number_x = LEFT_X
    title_x = LEFT_X + 20 if number else LEFT_X
    page_x = RIGHT_X - pdfmetrics.stringWidth(page_text, FONT_TIMES, FONT_SIZE)

    if number:
        c.drawString(number_x, y, number)

    max_title_width = page_x - title_x - 18
    title_lines = wrap_text_to_lines(title, FONT_TIMES, FONT_SIZE, max_title_width)

    line_gap = 16
    current_y = y

    for index, line in enumerate(title_lines):
        c.drawString(title_x, current_y, line)

        if index == len(title_lines) - 1:
            title_width = pdfmetrics.stringWidth(line, FONT_TIMES, FONT_SIZE)
            dot_start = title_x + title_width + 4
            dot_end = page_x - 4

            if dot_end > dot_start:
                dot_width = pdfmetrics.stringWidth(".", FONT_TIMES, FONT_SIZE)
                dot_count = int((dot_end - dot_start) / dot_width)
                c.drawString(dot_start, current_y, "." * dot_count)

            c.drawString(page_x, current_y, page_text)

        current_y -= line_gap

    return current_y - 8
def make_contents_pages(page_width, page_height, data, rows):
    job = data.get("job") or {}
    vessel = data.get("vessel") or {}
    report = data.get("reportInfo") or {}

    vessel_name = vessel.get("vesselName") or job.get("vesselName")
    company_name = report.get("companyName")

    pages = []

    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    draw_common_header_footer(
        c,
        vessel_name=vessel_name,
        company_name=company_name,
        page_no_text="Page | 1"
    )

    _draw_center_title(c, page_width)

    y = START_Y
    page_number = 1

    for row in rows:
        if y <= BOTTOM_Y:
            c.showPage()
            page_number += 1

            draw_common_header_footer(
                c,
                vessel_name=vessel_name,
                company_name=company_name,
                page_no_text=f"Page | {page_number}"
            )

            _draw_center_title(c, page_width)
            y = START_Y

        y = _draw_toc_row(
            c,
            y,
            row.get("number"),
            row.get("title"),
            row.get("pageNo")
        )

    c.save()
    packet.seek(0)

    return list(PdfReader(packet).pages)