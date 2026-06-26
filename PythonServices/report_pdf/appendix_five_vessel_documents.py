from io import BytesIO

from pypdf import PdfReader
from reportlab.lib import colors
from reportlab.pdfgen import canvas

from report_pdf.appendix_two_attachments import make_appendix_two_attachment_pages
from report_pdf.collection_information import get_collection_points
from report_pdf.helpers import (
    FONT_TIMES,
    draw_common_header_footer,
    wrap_text_to_lines,
)


ERASE_X = 80
ERASE_BOTTOM_Y = 100
ERASE_TOP_Y = 740

BULLET_X = 90
BULLET_TEXT_X = 102
BULLET_START_Y = 735
RIGHT_X = 540
BOTTOM_Y = 60
FONT_SIZE = 12
LEADING = 15


def _erase_body(c, page_width):
    c.setFillColor(colors.white)
    c.rect(
        ERASE_X,
        ERASE_BOTTOM_Y,
        page_width - ERASE_X,
        ERASE_TOP_Y - ERASE_BOTTOM_Y,
        stroke=0,
        fill=1
    )


def _draw_bullet_point(c, y, text):
    lines = wrap_text_to_lines(text, FONT_TIMES, FONT_SIZE, RIGHT_X - BULLET_TEXT_X)

    required_height = len(lines) * LEADING
    if y - required_height < BOTTOM_Y:
        return y, False

    c.setFont(FONT_TIMES, FONT_SIZE + 4)
    c.setFillColor(colors.black)
    c.drawString(BULLET_X, y - 1, u"\u2022")

    c.setFont(FONT_TIMES, FONT_SIZE)

    for index, line in enumerate(lines):
        c.drawString(BULLET_TEXT_X, y - (index * LEADING), line)

    return y - required_height, True

def draw_appendix_five_generated_marker(c):
    c.saveState()
    c.setFillColor(colors.white)
    c.setFont("Helvetica", 1)
    c.drawString(1, 1, "IHM_APPENDIX_5_GENERATED")
    c.restoreState()

def _make_bullet_pages(page_width, page_height, data):
    job = data.get("job") or {}
    vessel = data.get("vessel") or {}
    report = data.get("reportInfo") or {}

    vessel_name = vessel.get("vesselName") or job.get("vesselName")
    company_name = report.get("companyName")

    appendix_five_points = (
    data.get("appendixFiveInformationPoints")
    or data.get("AppendixFiveInformationPoints")
    or []
)

    points = []
    for item in appendix_five_points:
        if isinstance(item, dict):
            text = item.get("description") or item.get("Description")
        else:
            text = str(item)

        if text and text.strip():
            points.append(text.strip())

    if not points:
        points = get_collection_points(data)

    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    _erase_body(c, page_width)
    draw_appendix_five_generated_marker(c)

    y = BULLET_START_Y

    for point in points:
        y, drawn = _draw_bullet_point(c, y, point)

        if not drawn:
            c.showPage()
            draw_appendix_five_generated_marker(c)
            draw_common_header_footer(
                c,
                vessel_name=vessel_name,
                company_name=company_name,
                page_no_text="Page | "
            )

            y = 760
            y, drawn = _draw_bullet_point(c, y, point)

        y -= 5

    c.save()
    packet.seek(0)

    return list(PdfReader(packet).pages)


def make_appendix_five_vessel_document_pages(page_width, page_height, data):
    pages = _make_bullet_pages(page_width, page_height, data)

    appendix_five_data = dict(data)
    appendix_five_data["appendixTwoAttachments"] = data.get("appendixFiveAttachments") or []

    attachment_pages = make_appendix_two_attachment_pages(
        page_width,
        page_height,
        appendix_five_data
    )

    pages.extend(attachment_pages)

    return pages