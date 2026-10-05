# -*- coding: utf-8 -*-
from io import BytesIO

from pypdf import PdfReader
from reportlab.lib import colors
from reportlab.pdfgen import canvas
from reportlab.lib.pagesizes import A3, landscape
from reportlab.pdfbase import pdfmetrics
from report_pdf.helpers import (
    FONT_TIMES,
    txt,
    wrap_text_to_lines,
    draw_common_header_footer,format_date
)

FONT_TIMES_BOLD = "Times-Bold"
LEFT_X = 70
RIGHT_X = 520
MID_X = 260
START_Y = 755
BOTTOM_Y = 60
FONT_SIZE = 12
LEADING = 15
MIN_ROW_HEIGHT = 28
CELL_PAD_X = 6
CELL_PAD_TOP = 7
CELL_PAD_BOTTOM = 7
BULLET_TEXT_OFFSET = 16

ERASE_TOP_Y = 740
ERASE_BOTTOM_Y = 200

A3_TABLE_LEFT_X = 70
A3_TABLE_RIGHT_MARGIN = 70
A3_TABLE_TOP_GAP = 90
A3_FOOTER_SAFE_Y = 60
A3_HEADER_HEIGHT = 64
A3_BODY_FONT_SIZE = 12
A3_HEADER_FONT_SIZE = 12
A3_LEADING = 14
A3_CELL_PAD_X = 3
# Extra vertical padding prevents long equipment names from touching or
# escaping the cell border when the row is wrapped to multiple lines.
A3_CELL_PAD_Y = 10
DEFAULT_SAMPLING_METHODOLOGY = [
    "Wet the Sampling location prior to cutting and allow it to harden after cutting to prevent scatter.",
    "Appropriate Safety procedures are followed during onboard survey.",
]

DEFAULT_PAINT_FRAGMENTS = (
    "Paint suspected to contain Lead & Lead Compounds are collected and analyzed from Deck area."
)

DEFAULT_LABORATORY = "CHEM VI LABORATORY SDN BHD."
DEFAULT_CHEMICAL_ANALYSIS_METHOD = "Refer Appendix 2"
DEFAULT_VISUAL_SAMPLING_LOCATION = "Refer to lists for visual / sampling check"


def get_value(*sources_and_keys):
    for source, key in sources_and_keys:
        if isinstance(source, dict):
            value = source.get(key)
            if value not in (None, ""):
                return txt(value)
    return ""

def erase_template_content(c, page_width):
    c.saveState()
    c.setFillColor(colors.white)
    c.setStrokeColor(colors.white)
    c.rect(
        0,
        ERASE_BOTTOM_Y,
        page_width,
        ERASE_TOP_Y - ERASE_BOTTOM_Y,
        fill=1,
        stroke=0,
    )
    c.restoreState()

def fmt_dimension(value):
    value = txt(value)
    return f"{value} m" if value else ""


def build_lbd(vessel):
    lbp = fmt_dimension(vessel.get("lbp") or vessel.get("lengthBetweenPerpendiculars"))
    breadth = fmt_dimension(vessel.get("breadth") or vessel.get("breadthM"))
    depth = fmt_dimension(vessel.get("depth"))

    return " x ".join(part for part in [lbp, breadth, depth] if part)


def get_report_value(report, key, fallback):
    return txt(report.get(key) or report.get(key.replace("_", "")) or fallback)


def build_visual_sampling_rows(data):
    vessel = data.get("vessel") or {}
    report = data.get("reportInfo") or data.get("report") or {}
    survey = data.get("survey") or {}
    survey_job = data.get("surveyJob") or data.get("surveyJobs") or data.get("job") or {}

    return [
        ("NAME OF THE VESSEL", get_value((vessel, "vesselName"), (survey_job, "vesselName"))),
        ("VESSEL TYPE", get_value((vessel, "vesselType"), (vessel, "type"))),
        ("PORT OF REGISTRY", get_value((vessel, "portOfRegistry"), (vessel, "registryPort"))),
        ("CLASS", get_value((vessel, "class"), (vessel, "classSociety"))),
        ("IMO NUMBER", get_value((vessel, "imoNumber"), (vessel, "imoNo"))),
        ("GROSS TONNAGE", get_value((vessel, "grossTonnage"), (vessel, "gt"))),
        ("L x B x D", build_lbd(vessel)),
        ("SHIP MANAGER", get_value((vessel, "shipManager"), (vessel, "managerName"))),
        ("CONTACT POINT", get_report_value(report, "shipManagerContact", "")),
        ("Site of Check", get_value((survey_job, "auditPort"), (survey_job, "Audit Port"))),
        ("Date of Survey", format_date(get_value((survey_job, "surveyDate"), (survey, "surveyDate"), (survey_job, "startDate"), (survey, "startdate")))),
        ("Hazmat Expert /Sampling Engineer", get_report_value(report, "hazmatExpert", "")),
        (
            "Sampling Methodology",
            [
                line.strip().lstrip("-* ").strip()
                for line in txt(
                    report.get("samplingMethodology")
                ).splitlines()
                if line.strip()
            ] or DEFAULT_SAMPLING_METHODOLOGY,
        ),
        (
            "Sampling of fragments of Paints",
            get_report_value(report, "samplingOfFragmentsOfPaints", DEFAULT_PAINT_FRAGMENTS),
        ),
        ("Laboratory", get_report_value(report, "laboratory", DEFAULT_LABORATORY)),
        (
            "Chemical Analysis Method",
            get_report_value(report, "chemicalAnalysisMethod", DEFAULT_CHEMICAL_ANALYSIS_METHOD),
        ),
        (
            "Location of Visual/Sampling Check",
            get_report_value(
                report,
                "locationOfVisualSamplingCheck",
                DEFAULT_VISUAL_SAMPLING_LOCATION,
            ),
        ),
    ]


def wrap_cell_value(value, width):
    if isinstance(value, list):
        blocks = []

        for item in value:
            text_width = width - (CELL_PAD_X * 2) - BULLET_TEXT_OFFSET
            wrapped = wrap_text_to_lines(txt(item), FONT_TIMES, FONT_SIZE, text_width)

            if wrapped:
                blocks.append(("bullet", wrapped))

        return blocks

    text_width = width - (CELL_PAD_X * 2)
    wrapped = wrap_text_to_lines(txt(value), FONT_TIMES, FONT_SIZE, text_width)

    return [("text", wrapped)]


def cell_required_height(value, width):
    blocks = wrap_cell_value(value, width)

    line_count = 0
    block_gap_count = 0

    for block_type, lines in blocks:
        line_count += len(lines)
        if block_type == "bullet":
            block_gap_count += 1

    text_height = line_count * LEADING
    gap_height = max(0, block_gap_count - 1) * 2

    return max(
        MIN_ROW_HEIGHT,
        text_height + gap_height + CELL_PAD_TOP + CELL_PAD_BOTTOM,
    )

def draw_cell_text(c, x, y, width, height, value):
    c.setFont(FONT_TIMES, FONT_SIZE)
    c.setFillColor(colors.black)

    blocks = wrap_cell_value(value, width)

    line_count = sum(len(lines) for _, lines in blocks)
    bullet_block_count = sum(1 for block_type, _ in blocks if block_type == "bullet")

    if line_count == 0:
        return

    text_height = line_count * LEADING
    gap_height = max(0, bullet_block_count - 1) * 2
    content_height = text_height + gap_height

    current_y = y - ((height - content_height) / 2) - FONT_SIZE

    for block_index, (block_type, lines) in enumerate(blocks):
        for index, line in enumerate(lines):
            if block_type == "bullet":
                if index == 0:
                    c.setFont(FONT_TIMES, FONT_SIZE + 4)
                    c.drawString(x + CELL_PAD_X, current_y - 1, u"\u2022")

                c.setFont(FONT_TIMES, FONT_SIZE)
                c.drawString(x + CELL_PAD_X + BULLET_TEXT_OFFSET, current_y, line)
            else:
                c.setFont(FONT_TIMES, FONT_SIZE)
                c.drawString(x + CELL_PAD_X, current_y, line)

            current_y -= LEADING

        if block_type == "bullet" and block_index < len(blocks) - 1:
            current_y -= 2


def draw_table_header_and_rows(c, rows, page_width, page_height, data):
    job = data.get("job") or {}
    vessel = data.get("vessel") or {}
    report = data.get("reportInfo") or data.get("report") or {}

    vessel_name = vessel.get("vesselName") or job.get("vesselName")
    company_name = report.get("companyName")

    y = START_Y

    for left, right in rows:
        left_height = cell_required_height(left, MID_X - LEFT_X)
        right_height = cell_required_height(right, RIGHT_X - MID_X)
        row_height = max(left_height, right_height)

        if y - row_height < BOTTOM_Y:
            c.showPage()
            draw_common_header_footer(
                c,
                vessel_name=vessel_name,
                company_name=company_name,
                page_no_text="Page | ",
            )
            y = START_Y

        c.setStrokeColor(colors.black)
        c.setLineWidth(0.5)

        c.rect(LEFT_X, y - row_height, RIGHT_X - LEFT_X, row_height)
        c.line(MID_X, y, MID_X, y - row_height)

        draw_cell_text(c, LEFT_X, y, MID_X - LEFT_X, row_height, left)
        draw_cell_text(c, MID_X, y, RIGHT_X - MID_X, row_height, right)
        y -= row_height


def make_visual_sampling_check_plan_pages(page_width, page_height, data):
    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    erase_template_content(c, page_width)

    rows = build_visual_sampling_rows(data)
    draw_table_header_and_rows(c, rows, page_width, page_height, data)

    c.save()
    packet.seek(0)

    reader = PdfReader(packet)
    pages = list(reader.pages)

    pages.extend(make_checkpoint_a3_pages(data))

    return pages

def get_checkpoint_value(row, *keys):
    if not isinstance(row, dict):
        return ""

    for key in keys:
        value = row.get(key)
        if value not in (None, ""):
            return txt(value)

    return "-"

def get_checkpoint_text(row, *keys):
    if not isinstance(row, dict):
        return ""

    for key in keys:
        value = row.get(key)
        if value not in (None, ""):
            text_value = txt(value).strip()
            if text_value and text_value != "-":
                return text_value

    return ""

def get_ihm_checkpoints(data):
    return (
        data.get("ihmCheckpoints")
        or data.get("ihmCheckpoint")
        or data.get("ihmcheckpoints")
        or data.get("ihm_checkpoints")
        or []
    )


def checkpoint_remarks(row):
    excel_remark = get_checkpoint_text(row, "remarks", "Remarks")
    site_remark = get_checkpoint_text(
        row,
        "locationRemarks",
        "LocationRemarks",
        "Location_Remark",
    )
    if excel_remark and site_remark:
        return f"{excel_remark} ({site_remark})"
    return excel_remark or site_remark or "-"


def checkpoint_row_values(row):
    return [
        get_checkpoint_value(row, "sample_No", "sampleNo", "Sample_No"),
        get_checkpoint_value(row, "tableType", "table_A_B", "tableAb", "Table_A_B"),
        get_checkpoint_value(row, "hazardousMaterial", "HazardousMaterial"),
        get_checkpoint_value(row, "location", "Location"),
        get_checkpoint_value(row, "equipmentName", "EquipmentName"),
        get_checkpoint_value(row, "component", "Component"),
        get_checkpoint_value(row, "quantityUnitKg", "QuantityUnitKg"),
        get_checkpoint_value(row, "quantityNo", "QuantityNo"),
        get_checkpoint_value(row, "quantityTotalKg", "QuantityTotalKg"),
        get_checkpoint_value(row, "manufacturerBrandName", "ManufacturerBrandName"),
        get_checkpoint_value(row, "documentAnalysisResult", "DocumentAnalysisResult"),
        get_checkpoint_value(row, "samplingCheck", "SamplingCheck"),
        get_checkpoint_value(row, "visualCheck", "VisualCheck"),
        get_checkpoint_value(row, "checkResult", "CheckResult"),
        checkpoint_remarks(row),
    ]


def scaled_checkpoint_col_widths(page_width):
    table_width = page_width - A3_TABLE_LEFT_X - A3_TABLE_RIGHT_MARGIN

    base_widths = [
        58, 58, 100, 110, 110, 110,
        50, 50, 55,
        110, 85,
        75, 60,
        100, 138,
    ]

    scale = table_width / sum(base_widths)
    return [width * scale for width in base_widths]


def wrap_a3_cell(value, width, font=FONT_TIMES, size=A3_BODY_FONT_SIZE):
    return _wrap_with_long_word_breaks(
        value,
        font,
        size,
        width - (A3_CELL_PAD_X * 2),
    )


def _wrap_with_long_word_breaks(value, font, size, max_width):
    """Wrap normal text and split unbreakable equipment names if necessary."""
    lines = []
    for paragraph in txt(value).splitlines() or [""]:
        words = paragraph.split() or [""]
        current = ""
        for word in words:
            if pdfmetrics.stringWidth(word, font, size) <= max_width:
                candidate = f"{current} {word}".strip()
                if not current or pdfmetrics.stringWidth(candidate, font, size) <= max_width:
                    current = candidate
                    continue

            if current:
                lines.append(current)
                current = ""

            # Break long words (for example underscore-separated equipment
            # names) so they never cross into the next cell.
            chunk = ""
            for character in word:
                candidate = chunk + character
                if chunk and pdfmetrics.stringWidth(candidate, font, size) > max_width:
                    lines.append(chunk)
                    chunk = character
                else:
                    chunk = candidate
            current = chunk

        if current or not lines:
            lines.append(current or "-")
    return lines or ["-"]


def a3_row_height(values, col_widths):
    max_lines = 1

    for value, width in zip(values, col_widths):
        lines = wrap_a3_cell(value, width)
        max_lines = max(max_lines, len(lines))

    return max(34, (max_lines * A3_LEADING) + (A3_CELL_PAD_Y * 2))


def draw_centered_wrapped_text(c, x1, y1, x2, y2, value, font=FONT_TIMES, size=A3_BODY_FONT_SIZE):
    lines = wrap_a3_cell(value, x2 - x1, font, size)

    c.setFont(font, size)
    c.setFillColor(colors.black)

    total_height = len(lines) * A3_LEADING
    y = y1 + ((y2 - y1) - total_height) / 2 + total_height - size

    center_x = x1 + ((x2 - x1) / 2)

    for line in lines:
        c.drawCentredString(center_x, y, line)
        y -= A3_LEADING


def draw_a3_cell(c, x1, y1, x2, y2, value, font=FONT_TIMES, size=A3_BODY_FONT_SIZE):
    c.setStrokeColor(colors.black)
    c.setLineWidth(0.5)
    c.rect(x1, y1, x2 - x1, y2 - y1)
    draw_centered_wrapped_text(c, x1, y1, x2, y2, value, font, size)


def draw_checkpoint_table_header(c, page_width, top_y, col_widths):
    x_positions = [A3_TABLE_LEFT_X]

    for width in col_widths:
        x_positions.append(x_positions[-1] + width)

    header_top = top_y
    header_mid = top_y - 32
    header_bottom = top_y - A3_HEADER_HEIGHT

    row_span_headers = {
        0: "Sample\nNo.",
        1: "Table\n(A/B)",
        2: "Hazardous\nmaterials",
        3: "Location",
        4: "Name of\nequipment",
        5: "Component",
        9: "Manufacturer /\nbrand name",
        10: "Result of\ndocument\nanalysis",
        13: "Result of\nCheck",
        14: "Remarks",
    }

    for index, label in row_span_headers.items():
        draw_a3_cell(
            c,
            x_positions[index],
            header_bottom,
            x_positions[index + 1],
            header_top,
            label,
            font=FONT_TIMES_BOLD,
            size=A3_HEADER_FONT_SIZE,
        )

    draw_a3_cell(c, x_positions[6], header_mid, x_positions[9], header_top, "Quantity of hazmat",font=FONT_TIMES_BOLD, size=A3_HEADER_FONT_SIZE)
    draw_a3_cell(c, x_positions[6], header_bottom, x_positions[7], header_mid, "Unit\n(kg)",font=FONT_TIMES_BOLD, size=A3_HEADER_FONT_SIZE)
    draw_a3_cell(c, x_positions[7], header_bottom, x_positions[8], header_mid, "No.", font=FONT_TIMES_BOLD,size=A3_HEADER_FONT_SIZE)
    draw_a3_cell(c, x_positions[8], header_bottom, x_positions[9], header_mid, "Total\n(kg)",font=FONT_TIMES_BOLD, size=A3_HEADER_FONT_SIZE)

    draw_a3_cell(c, x_positions[11], header_mid, x_positions[13], header_top, "Procedure of check",font=FONT_TIMES_BOLD, size=A3_HEADER_FONT_SIZE)
    draw_a3_cell(c, x_positions[11], header_bottom, x_positions[12], header_mid, "Sampling\ncheck",font=FONT_TIMES_BOLD, size=A3_HEADER_FONT_SIZE)
    draw_a3_cell(c, x_positions[12], header_bottom, x_positions[13], header_mid, "Visual\ncheck",font=FONT_TIMES_BOLD, size=A3_HEADER_FONT_SIZE)

    return header_bottom, x_positions


def draw_checkpoint_body_row(c, y, row_height, values, x_positions):
    bottom_y = y - row_height

    for index, value in enumerate(values):
        draw_a3_cell(
            c,
            x_positions[index],
            bottom_y,
            x_positions[index + 1],
            y,
            value,
            size=A3_BODY_FONT_SIZE,
        )

    return bottom_y


def start_checkpoint_a3_page(c, page_width, page_height, data):
    job = data.get("job") or {}
    vessel = data.get("vessel") or {}
    report = data.get("reportInfo") or data.get("report") or {}

    vessel_name = vessel.get("vesselName") or job.get("vesselName")
    company_name = report.get("companyName")

    draw_common_header_footer(
        c,
        vessel_name=vessel_name,
        company_name=company_name,
        page_no_text="Page | ",
    )

    col_widths = scaled_checkpoint_col_widths(page_width)
    table_top_y = page_height - A3_TABLE_TOP_GAP

    return draw_checkpoint_table_header(c, page_width, table_top_y, col_widths)


def start_checkpoint_legend_page(c, data):
    job = data.get("job") or {}
    vessel = data.get("vessel") or {}
    report = data.get("reportInfo") or data.get("report") or {}

    vessel_name = vessel.get("vesselName") or job.get("vesselName")
    company_name = report.get("companyName")

    draw_common_header_footer(
        c,
        vessel_name=vessel_name,
        company_name=company_name,
        page_no_text="Page | ",
    )


def make_checkpoint_a3_pages(data):
    checkpoints = [
        checkpoint
        for checkpoint in get_ihm_checkpoints(data)
        if any(value not in ("", "-") for value in checkpoint_row_values(checkpoint))
    ]
    # The API includes the original Excel row number. Keep the PDF table in
    # Excel order even when Sample_No prefixes/numbers are regenerated.
    checkpoints.sort(key=lambda row: (
        int(row.get("sourceRowNumber") or row.get("SourceRowNumber") or 2**31 - 1),
        int(row.get("id") or row.get("Id") or 2**31 - 1),
    ))
    print("IHM CHECKPOINT COUNT:", len(checkpoints))
    if not checkpoints:
        return []

    page_width, page_height = landscape(A3)

    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    y, x_positions = start_checkpoint_a3_page(c, page_width, page_height, data)
    col_widths = scaled_checkpoint_col_widths(page_width)

    for checkpoint in checkpoints:
        values = checkpoint_row_values(checkpoint)
        row_height = a3_row_height(values, col_widths)

        if y - row_height < A3_FOOTER_SAFE_Y:
            c.showPage()
            y, x_positions = start_checkpoint_a3_page(c, page_width, page_height, data)

        y = draw_checkpoint_body_row(c, y, row_height, values, x_positions)
    
    legend_y = y - 30

    if legend_y < A3_FOOTER_SAFE_Y:
        c.showPage()
        start_checkpoint_legend_page(c, data)
        legend_y = page_height - A3_TABLE_TOP_GAP

    c.setFont("Times-Roman", 12)
    c.setFillColor(colors.black)

    c.drawString(
        A3_TABLE_LEFT_X,
        legend_y,
        "Yes = Contained Hazardous Materials"
    )

    c.drawString(
        A3_TABLE_LEFT_X,
        legend_y - 18,
        "No = Not Contained Hazardous Materials"
    )

    c.drawString(
        A3_TABLE_LEFT_X,
        legend_y - 36,
        "V = Visual Check"
    )

    c.drawString(
        A3_TABLE_LEFT_X,
        legend_y - 54,
        "S = Sampling Check"
    )
    c.save()
    packet.seek(0)

    reader = PdfReader(packet)
    return list(reader.pages)

def make_onboard_visual_sampling_overlay(page_width, page_height, data):
    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    survey = data.get("survey") or {}
    survey_job = data.get("surveyJob") or data.get("surveyJobs") or data.get("job") or {}
    report = data.get("reportInfo") or data.get("report") or {}

    x = 180

    survey_date = format_date(
        get_value((survey_job, "surveyDate"), (survey, "surveyDate"), (survey_job, "surveyDate"), (survey, "surveyDate"), (survey_job, "startDate"), (survey, "startdate"))
    )
    audit_port = get_value((survey_job, "auditPort"), (survey_job, "Audit Port"))
    hazmat_expert = get_report_value(report, "hazmatExpert", "")

    c.setFont(FONT_TIMES, 12)
    c.setFillColor(colors.black)

    c.drawString(x, 740, f": {survey_date}")
    c.drawString(x, 715, f": {audit_port}")
    c.drawString(x, 690, f": {hazmat_expert}")
    c.save()
    packet.seek(0)

    reader = PdfReader(packet)
    return reader.pages[0] if reader.pages else None
