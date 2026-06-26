from io import BytesIO

from pypdf import PdfReader
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfgen import canvas

from report_pdf.appendix_two_attachments import make_appendix_two_attachment_pages
from report_pdf.helpers import draw_common_header_footer, FONT_TIMES


def _wrap_center_title(text, font, size, max_width):
    words = text.split()
    lines = []
    current = ""

    for word in words:
        test = f"{current} {word}".strip()

        if pdfmetrics.stringWidth(test, font, size) <= max_width:
            current = test
        else:
            if current:
                lines.append(current)
            current = word

    if current:
        lines.append(current)

    return lines

def _group_custom_appendices(data):
    rows = data.get("customAppendices") or []
    groups = {}

    for row in rows:
        appendix_no = row.get("appendixNo")
        appendix_title = row.get("appendixTitle") or "ADDITIONAL DOCUMENTS"

        if appendix_no is None:
            continue

        key = int(appendix_no)

        if key not in groups:
            groups[key] = {
                "appendixNo": key,
                "appendixTitle": appendix_title,
                "attachments": []
            }

        groups[key]["attachments"].append(row)

    return [groups[key] for key in sorted(groups.keys())]


def _make_appendix_title_page(page_width, page_height, data, appendix_no, appendix_title, page_number):
    packet = BytesIO()
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
    page_no_text=f"Page | {page_number}"
    )

    title = f"APPENDIX {appendix_no} - {appendix_title}".upper()

    font = "Times-Bold"
    font_size = 14
    max_width = page_width - 140
    line_gap = 16

    lines = _wrap_center_title(title, font, font_size, max_width)

    total_height = len(lines) * line_gap
    start_y = (page_height / 2) + (total_height / 2) - font_size

    c.setFont(font, font_size)

    for index, line in enumerate(lines):
        line_width = pdfmetrics.stringWidth(line, font, font_size)

        c.drawString(
            (page_width - line_width) / 2,
            start_y - (index * line_gap),
            line
        )

    c.save()
    packet.seek(0)

    return PdfReader(packet).pages[0]
def make_custom_appendix_pages(page_width, page_height, data, start_page_number=1):
    pages = []
    page_number = start_page_number

    for appendix in _group_custom_appendices(data):
        appendix_no = appendix["appendixNo"]
        appendix_title = appendix["appendixTitle"]
        attachments = appendix["attachments"]

        pages.append(
            _make_appendix_title_page(
                page_width,
                page_height,
                data,
                appendix_no,
                appendix_title,
                page_number
            )
        )

        page_number += 1

        attachment_data = dict(data)
        attachment_data["appendixTwoAttachments"] = attachments

        attachment_pages = make_appendix_two_attachment_pages(
            page_width,
            page_height,
            attachment_data
        )

        pages.extend(attachment_pages)
        page_number += len(attachment_pages)

    return pages