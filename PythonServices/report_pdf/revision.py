from io import BytesIO

from pypdf import PdfReader
from reportlab.pdfgen import canvas
from reportlab.lib import colors
from reportlab.pdfbase import pdfmetrics

from report_pdf.helpers import (
    FONT_TIMES,
    txt,
    format_date,
    split_text_to_lines,
    draw_common_header_footer,calculate_row_height,wrap_text_to_lines,
)

def get_revision_row_height(row, col_x):
    values = [
        row["uid"],
        row["revision"],
        row["date"],
        row["description"],
        row["prepared"],
        row["checked"],
        row["approved"],
    ]

    widths = [
        col_x["revision"] - col_x["uid"],
        col_x["date"] - col_x["revision"],
        col_x["description"] - col_x["date"],
        col_x["prepared"] - col_x["description"],
        col_x["checked"] - col_x["prepared"],
        col_x["approved"] - col_x["checked"],
        col_x["right_edge"] - col_x["approved"],
    ]

    return calculate_row_height(values, widths, min_height=45, font=FONT_TIMES, size=12)

def get_revision_value(rev, key):
    if not isinstance(rev, dict):
        return ""
    return rev.get(key)

def draw_cell_text(c, text, x1, y_bottom, x2, y_top, font=FONT_TIMES, size=12, bold=False):
    value = txt(text)
    if not value:
        return

    use_font = "Times-Bold" if bold else font
    cell_w = x2 - x1
    cell_h = y_top - y_bottom

    lines = wrap_text_to_lines(value, use_font, size, cell_w - 4)
    if not lines:
        return

    leading = size + 2
    total_text_h = len(lines) * leading
    start_y = y_bottom + (cell_h - total_text_h) / 2 + (len(lines) - 1) * leading

    c.setFont(use_font, size)
    c.setFillColor(colors.black)

    for i, line in enumerate(lines):
        line_width = pdfmetrics.stringWidth(line, use_font, size)
        x = x1 + (cell_w - line_width) / 2
        y = start_y - (i * leading)
        c.drawString(x, y, line)


def draw_revision_row_border(c, y_top, row_h, col_x):
    y_bottom = y_top - row_h

    c.setStrokeColor(colors.black)
    c.setLineWidth(0.5)

    c.rect(
        col_x["uid"],
        y_bottom,
        col_x["right_edge"] - col_x["uid"],
        row_h,
        stroke=1,
        fill=0
    )

    for key in ["revision", "date", "description", "prepared", "checked", "approved"]:
        x = col_x[key]
        c.line(x, y_bottom, x, y_top)

def draw_revision_table_header(c, y_top, row_h, col_x):
    draw_revision_row_border(c, y_top, row_h, col_x)

    y_bottom = y_top - row_h

    headers = [
        ("UID", "uid", "revision"),
        ("REV", "revision", "date"),
        ("DATE", "date", "description"),
        ("DESCRIPTION", "description", "prepared"),
        ("PRE", "prepared", "checked"),
        ("CHK", "checked", "approved"),
        ("APR", "approved", "right_edge"),
    ]

    for label, left_key, right_key in headers:
        draw_cell_text(
            c,
            label,
            col_x[left_key],
            y_bottom,
            col_x[right_key],
            y_top,
            font=FONT_TIMES,
            size=12,
            bold=True
        )


def draw_revision_row(c, row, y_top, row_h, col_x, draw_border=False):
    y_bottom = y_top - row_h

    if draw_border:
        draw_revision_row_border(c, y_top, row_h, col_x)

    draw_cell_text(c, row["uid"], col_x["uid"], y_bottom, col_x["revision"], y_top)
    draw_cell_text(c, row["revision"], col_x["revision"], y_bottom, col_x["date"], y_top)
    draw_cell_text(c, row["date"], col_x["date"], y_bottom, col_x["description"], y_top)
    draw_cell_text(c, row["description"], col_x["description"], y_bottom, col_x["prepared"], y_top)
    draw_cell_text(c, row["prepared"], col_x["prepared"], y_bottom, col_x["checked"], y_top)
    draw_cell_text(c, row["checked"], col_x["checked"], y_bottom, col_x["approved"], y_top)
    draw_cell_text(c, row["approved"], col_x["approved"], y_bottom, col_x["right_edge"], y_top)

def draw_revision_page(c, page_width, page_height, data, start_row_index=0, first_template_page=False):
    job = data.get("job") or {}
    vessel = data.get("vessel") or {}
    report = data.get("reportInfo") or {}

    vessel_name = vessel.get("vesselName") or job.get("vesselName")
    company_name = report.get("companyName")

    rows = build_revision_display_rows(data)

    # Always show at least 4 blank rows
    if len(rows) < 4:
        for _ in range(4 - len(rows)):
            rows.append({
                "uid": "",
                "revision": "",
                "date": "",
                "description": "",
                "prepared": "",
                "checked": "",
                "approved": ""
            })

    col_x = {
        "uid": 72.5,
        "revision": 122.5,
        "date": 159,
        "description": 227,
        "prepared": 431.5,
        "checked": 472.5,
        "approved": 513,
        "right_edge": 558.5,
    }

    header_h = 45
    header_top = 755
    bottom_limit = 70

    # If this is an extra generated page, draw common header/footer.
    # On reference page, REVISION HISTORY title already exists.
    if not first_template_page:
        draw_common_header_footer(
            c,
            vessel_name=vessel_name,
            company_name=company_name,
            page_no_text="Page | "
        )

    draw_revision_table_header(c, header_top, header_h, col_x)

    y_top = header_top - header_h
    row_index = start_row_index

    while row_index < len(rows):
        current_row_h = get_revision_row_height(rows[row_index], col_x)

        if y_top - current_row_h < bottom_limit:
            break

        draw_revision_row(
            c,
            rows[row_index],
            y_top,
            current_row_h,
            col_x,
            draw_border=True
        )

        row_index += 1
        y_top -= current_row_h

    return row_index

def make_blank_page_with_revision_rows(page_width, page_height, data, start_row_index):
    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    next_index = draw_revision_page(
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

def get_first_revision_page_next_index(data):
    rows = build_revision_display_rows(data)

    if len(rows) < 4:
        for _ in range(4 - len(rows)):
            rows.append({
                "uid": "",
                "revision": "",
                "date": "",
                "description": "",
                "prepared": "",
                "checked": "",
                "approved": ""
            })

    col_x = {
        "uid": 72.5,
        "revision": 122.5,
        "date": 159,
        "description": 227,
        "prepared": 431.5,
        "checked": 472.5,
        "approved": 513,
        "right_edge": 558.5,
    }

    header_h = 45
    y_top = 755 - header_h
    next_index = 0

    while next_index < len(rows):
        current_row_h = get_revision_row_height(rows[next_index], col_x)

        if y_top - current_row_h < 70:
            break

        next_index += 1
        y_top -= current_row_h

    return next_index
def build_revision_display_rows(data):
    report = data.get("reportInfo") or {}
    revisions = data.get("revisions") or []

    rows = []

    for rev in revisions:
        uid = get_revision_value(rev, "uid") or report.get("uid")
        revision_no = get_revision_value(rev, "revisionNo")
        if revision_no is None:
            revision_no = report.get("revisionNo")

        revision_date = get_revision_value(rev, "revisionDate") or report.get("documentDate")
        description = get_revision_value(rev, "description")

        if not txt(description):
            continue

        prepared = get_revision_value(rev, "preparedBy") or report.get("preparedBy")
        checked = get_revision_value(rev, "checkedBy") or report.get("checkedBy")
        approved = get_revision_value(rev, "approvedBy") or report.get("approvedBy")

        rows.append({
            "uid": uid,
            "revision": revision_no,
            "date": format_date(revision_date),
            "description": txt(description).upper(),
            "prepared": prepared,
            "checked": checked,
            "approved": approved
        })

    return rows

