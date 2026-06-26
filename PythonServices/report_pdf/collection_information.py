from io import BytesIO

from pypdf import PdfReader
from reportlab.pdfgen import canvas
from reportlab.lib import colors

from report_pdf.helpers import (
    FONT_TIMES,
    txt,
    wrap_text_to_lines,
    draw_common_header_footer,
)


LEFT_X = 70
RIGHT_X = 540
INTRO_Y = 742
BULLET_X = 90
BULLET_TEXT_X = 102
BULLET_START_Y = 720
BOTTOM_Y = 60
FONT_SIZE = 12
LEADING = 15


def get_collection_points(data):
    points = data.get("necessaryInformationPoints") or data.get("collectionInformationPoints") or []

    result = []
    for item in points:
        if isinstance(item, dict):
            value = item.get("description") or item.get("point") or item.get("text")
        else:
            value = item

        value = txt(value)
        if value:
            result.append(value)

    return result


def draw_wrapped_lines(c, x, y, text, width, font=FONT_TIMES, size=FONT_SIZE, leading=LEADING):
    lines = wrap_text_to_lines(text, font, size, width)

    c.setFont(font, size)
    c.setFillColor(colors.black)

    for line in lines:
        c.drawString(x, y, line)
        y -= leading

    return y


def draw_bullet_point(c, y, text):
    lines = wrap_text_to_lines(text, FONT_TIMES, FONT_SIZE, RIGHT_X - BULLET_TEXT_X)

    required_height = len(lines) * LEADING
    if y - required_height < BOTTOM_Y:
        return y, False

    c.setFont(FONT_TIMES, FONT_SIZE+4)
    c.setFillColor(colors.black)

    c.drawString(BULLET_X, y-1, u"\u2022")

    c.setFont(FONT_TIMES, FONT_SIZE)

    for index, line in enumerate(lines):
        c.drawString(BULLET_TEXT_X, y - (index * LEADING), line)

    return y - required_height, True

def draw_intro_text(c, x, y, vessel_name, width):
    prefix = 'The following documents and drawings were collected from the vessel "'
    name = txt(vessel_name).upper()
    suffix = '".'

    c.setFillColor(colors.black)
    c.setFont(FONT_TIMES, FONT_SIZE)
    c.drawString(x, y, prefix)

    prefix_width = c.stringWidth(prefix, FONT_TIMES, FONT_SIZE)

    c.setFont("Times-Italic", FONT_SIZE)
    c.drawString(x + prefix_width, y, name)

    name_width = c.stringWidth(name, "Times-Italic", FONT_SIZE)

    c.setFont(FONT_TIMES, FONT_SIZE)
    c.drawString(x + prefix_width + name_width, y, suffix)

    return y - LEADING

def make_collection_information_pages(page_width, page_height, data):
    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    job = data.get("job") or {}
    vessel = data.get("vessel") or {}
    report = data.get("reportInfo") or {}

    vessel_name = vessel.get("vesselName") or job.get("vesselName")
    company_name = report.get("companyName")

    y = draw_intro_text(
    c,
    LEFT_X,
    INTRO_Y,
    vessel_name,
    RIGHT_X - LEFT_X,
)
    points = get_collection_points(data)

    y = min(y - 5, BULLET_START_Y)

    if not points:
        points = ["-"]

    for point in points:
        y, drawn = draw_bullet_point(c, y, point)

        if not drawn:
            c.showPage()
            draw_common_header_footer(
                c,
                vessel_name=vessel_name,
                company_name=company_name,
                page_no_text="Page | "
            )
            y = 760
            y, drawn = draw_bullet_point(c, y, point)

        y -= 5

    c.save()
    packet.seek(0)

    reader = PdfReader(packet)
    return list(reader.pages)