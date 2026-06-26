from reportlab.lib import colors

from report_pdf.helpers import (
    FONT_TIMES,
    txt,
    format_date,
    draw_text,
    split_text_to_lines,
)



def draw_wrapped_block(c, x, y, text, width, font=FONT_TIMES, size=12, leading=15, bold=False):
    value = txt(text)
    if not value:
        return y

    use_font = "Times-Bold" if bold else font
    lines = split_text_to_lines(value, use_font, size, width, max_lines=20)

    c.setFont(use_font, size)
    c.setFillColor(colors.black)

    for line in lines:
        c.drawString(x, y, line)
        y -= leading

    return y

def draw_introduction_page(c, page_width, page_height, data):
    report = data.get("reportInfo") or {}

    print("REPORT INFO:", report)
    print("NO FINDINGS:", report.get("hazardousMaterialNoFindings"))
    print("YES FINDINGS:", report.get("hazardousMaterialFindings"))
    print("LAB REPORT:", report.get("labReportNo"))

    summary = data.get("ihmSummary") or {}

    sampling_count = summary.get("samplingCheckCount", 0)
    visual_count = summary.get("visualCheckCount", 0)

    draw_text(
        c,
        73,
        480,
        f"Total of {sampling_count} Nos. bulk samples were collected and {visual_count} Nos. were visually identified.",
        font=FONT_TIMES,
        size=12
    )

    next_y = 480 - 15 - 15

    next_y = draw_wrapped_block(
        c,
        73,
        next_y,
        report.get("hazardousMaterialNoFindings"),
        width=449,
        font=FONT_TIMES,
        size=12,
        leading=15,
        bold=True
    )

    next_y -= 20

    next_y = draw_wrapped_block(
        c,
        73,
        next_y,
        report.get("hazardousMaterialFindings"),
        width=449,
        font=FONT_TIMES,
        size=12,
        leading=15,
        bold=True
    )


    next_y -= 20

    lab_report_no = report.get("labReportNo")
    if txt(lab_report_no):
        draw_text(
            c,
            73,
            next_y,
            f"Refer to attached Lab Test Report No : {txt(lab_report_no)}",
            font=FONT_TIMES,
            size=12
        )

def draw_lab_report_reference_page(c, page_width, page_height, data):
    report = data.get("reportInfo") or {}

    draw_text(
        c,
        140,
        623,
        report.get("labReportNo"),
        font=FONT_TIMES,
        size=12
    )

    draw_text(
        c,
        155,
        600,
        format_date(report.get("labReportingDate")),
        font=FONT_TIMES,
        size=12
    )

