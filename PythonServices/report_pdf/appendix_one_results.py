from io import BytesIO

from pypdf import PdfReader
from reportlab.lib import colors
from reportlab.pdfgen import canvas

from report_pdf.helpers import (
    FONT_TIMES,
    txt,
    wrap_text_to_lines,
    draw_common_header_footer,
)


LEFT_X = 70
LABEL_X = 80
TEXT_X = 105
RIGHT_X = 520
START_Y = 720
BOTTOM_Y = 65
FONT_SIZE = 12
LEADING = 14
POINT_GAP = 12

ERASE_TOP_Y = 739
ERASE_BOTTOM_Y = 70

def appendix_label(index):
    value = ""
    index += 1

    while index:
        index, remainder = divmod(index - 1, 26)
        value = chr(97 + remainder) + value

    return f"{value}."

DEFAULT_APPENDIX_ONE_POINTS = [
    ("Asbestos", "Refer Appendix 2 for Analysis report for Asbestos."),
    ("Polychlorinated biphenyls (PCBs)", "Refer Appendix 2 for Analysis report for Polychlorinated biphenyls (PCBs)"),
    ("Lead and Lead Compounds", "Refer Appendix 2 for Analysis report for Lead and Lead Compounds."),
    ("Cadmium and Cadmium Compounds.", "Refer Appendix 2 for Analysis report for Cadmium and Cadmium Compounds."),
    ("Polychlorinated Napthalenes (PCNs)", "Refer Appendix 2 for Analysis report for Polychlorinated Napthalenes (PCNs)"),
    ("HBCDD - Brominated Flame Retardant", "Refer Appendix 2 for Analysis report for Polychlorinated biphenyls (HBCDD)"),
    ("PFOS - Perfluro Octane Sulfonic Acid", "Refer Appendix 2 for Analysis report for Perfluro Octane Sulfonic Acid (PFOS)."),
    ("Polybrominated biphenyl (PBBs)", "Refer Appendix 2 for Analysis report for Polybrominated biphenyl (PBBs)"),
    ("Polybrominated diphenyl ethers (PBDEs)", "Refer Appendix 2 for Analysis report for Polybrominated diphenyl ethers (PBDEs)"),
    ("Hexavalent Chromium and Hexavalent Chromium Compounds.", "Refer Appendix 2 for Analysis report for Hexavalent Chromium and Hexavalent Chromium Compounds."),
    ("SCCP - Certain shortchain chlorinated paraffins.", "Refer Appendix 2 for Analysis report for Certain shortchain chlorinated paraffins. (SCCP)."),
    ("Mercury & Mercury Compounds", "Refer Appendix 2 for Analysis report for Mercury & Mercury Compounds"),
    ("Ozone Depleting Substances (ODSs)", "Refer Appendix 2 for Analysis report for Ozone Depleting Substances (ODSs)"),
]


def get_appendix_points(data):
    rows = data.get("appendixOneResultPoints") or []

    if rows:
        return [
        (
            txt(row.get("title")),
            txt(row.get("description")),
        )
        for row in rows
        if isinstance(row, dict)
]

    return DEFAULT_APPENDIX_ONE_POINTS


def erase_existing_content(c, page_width):
    c.saveState()
    c.setFillColor(colors.white)
    c.rect(
        0,
        ERASE_BOTTOM_Y,
        page_width,
        ERASE_TOP_Y - ERASE_BOTTOM_Y,
        fill=1,
        stroke=0,
    )
    c.restoreState()


def point_height(title, description):
    title_lines = wrap_text_to_lines(title, FONT_TIMES, FONT_SIZE, RIGHT_X - TEXT_X)
    desc_lines = wrap_text_to_lines(description, FONT_TIMES, FONT_SIZE, RIGHT_X - TEXT_X)

    return (len(title_lines) + len(desc_lines)) * LEADING + POINT_GAP


def draw_point(c, y, label, title, description):
    c.setFont(FONT_TIMES, FONT_SIZE)
    c.setFillColor(colors.black)

    c.drawString(LABEL_X, y, label)

    for line in wrap_text_to_lines(title, FONT_TIMES, FONT_SIZE, RIGHT_X - TEXT_X):
        c.drawString(TEXT_X, y, line)
        y -= LEADING

    for line in wrap_text_to_lines(description, FONT_TIMES, FONT_SIZE, RIGHT_X - TEXT_X):
        c.drawString(TEXT_X, y, line)
        y -= LEADING

    return y - POINT_GAP


def make_appendix_one_results_pages(page_width, page_height, data, first_template_page=None):
    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    job = data.get("job") or {}
    vessel = data.get("vessel") or {}
    report = data.get("reportInfo") or data.get("report") or {}

    vessel_name = vessel.get("vesselName") or job.get("vesselName")
    company_name = report.get("companyName")

    erase_existing_content(c, page_width)

    points = get_appendix_points(data)
    y = START_Y

    for index, (title, description) in enumerate(points):
        label = appendix_label(index)
        needed = point_height(title, description)

        if y - needed < BOTTOM_Y:
            c.showPage()
            draw_common_header_footer(
                c,
                vessel_name=vessel_name,
                company_name=company_name,
                page_no_text="Page | ",
            )
            y = START_Y

        y = draw_point(c, y, label, title, description)

    c.save()
    packet.seek(0)

    reader = PdfReader(packet)
    return list(reader.pages)