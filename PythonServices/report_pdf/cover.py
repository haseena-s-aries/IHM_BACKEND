from reportlab.pdfbase import pdfmetrics

from report_pdf.helpers import (
    FONT_ARIAL,
    FONT_ARIAL_BOLD,
    FONT_TIMES,
    txt,
    format_date,
    draw_text,
    draw_centered_text,
    draw_image_in_box,
    draw_link_text,
)

from reportlab.lib import colors
from report_pdf.helpers import draw_link_text



def draw_company_block(c, report):
    block_left = 43
    block_right = 285
    block_top = 174
    block_bottom = 108

    logo_left = 205
    logo_right = 285
    logo_bottom = 112

    company_name = txt(report.get("companyName")).upper()
    company_address = txt(report.get("companyAddress"))
    company_tel = txt(report.get("companyTel"))
    company_fax = txt(report.get("companyFax"))
    company_website = txt(report.get("companyWebsite"))
    company_email = txt(report.get("companyEmail"))
    company_logo = report.get("companyLogoPath")

    def wrap_to_width(text, font, size, max_width):
        text = " ".join(txt(text).replace("\r", " ").replace("\n", " ").split())

        if not text:
            return []

        lines = []
        current = ""

        for word in text.split():
            candidate = f"{current} {word}".strip()

            if pdfmetrics.stringWidth(candidate, font, size) <= max_width:
                current = candidate
                continue

            if current:
                lines.append(current)
                current = ""

            # Handle a single word longer than the available width.
            part = ""

            for character in word:
                candidate_part = part + character

                if pdfmetrics.stringWidth(
                    candidate_part,
                    font,
                    size
                ) <= max_width:
                    part = candidate_part
                else:
                    if part:
                        lines.append(part)
                    part = character

            current = part

        if current:
            lines.append(current)

        return lines

    # Company name can use the complete width above the logo.
    name_size = 12
    name_width = block_right - block_left

    while name_size > 8:
        name_lines = wrap_to_width(
            company_name,
            FONT_ARIAL_BOLD,
            name_size,
            name_width
        )

        if len(name_lines) <= 2:
            break

        name_size -= 0.5

    name_lines = wrap_to_width(
        company_name,
        FONT_ARIAL_BOLD,
        name_size,
        name_width
    )[:2]

    current_y = block_top

    for line in name_lines:
        draw_text(
            c,
            block_left,
            current_y,
            line,
            font=FONT_ARIAL_BOLD,
            size=name_size
        )
        current_y -= name_size + 1

    current_y -= 2

    # Remaining information stays left of the logo.
    body_width = logo_left - block_left - 5
    available_height = current_y - block_bottom

    body_size = 8

    def make_body_lines(size):
        rows = []

        for line in wrap_to_width(
            company_address,
            FONT_ARIAL,
            size,
            body_width
        ):
            rows.append(("text", line))

        for label, value in (
            ("Tel", company_tel),
            ("Fax", company_fax)
        ):
            if value:
                for line in wrap_to_width(
                    f"{label}: {value}",
                    FONT_ARIAL,
                    size,
                    body_width
                ):
                    rows.append(("text", line))

        if company_website:
            for line in wrap_to_width(
                company_website,
                FONT_ARIAL,
                size,
                body_width
            ):
                rows.append(("link", line))

        if company_email:
            email_lines = [
                email.strip()
                for email in company_email.replace("\r", "\n").split("\n")
                for email in email.split(",")
                if email.strip()
            ]

            for email in email_lines:
                for line in wrap_to_width(
                    email,
                    FONT_ARIAL,
                    size,
                    body_width
                ):
                    rows.append(("link", line))

        return rows

    body_lines = make_body_lines(body_size)

    while body_size > 5:
        line_height = body_size + 1

        if len(body_lines) * line_height <= available_height:
            break

        body_size -= 0.5
        body_lines = make_body_lines(body_size)

    line_height = body_size + 1

    for line_type, line in body_lines:
        if current_y - line_height < block_bottom:
            break

        if line_type == "link":
            draw_link_text(
                c,
                block_left,
                current_y,
                line,
                font=FONT_ARIAL,
                size=body_size
            )
        else:
            draw_text(
                c,
                block_left,
                current_y,
                line,
                font=FONT_ARIAL,
                size=body_size
            )

        current_y -= line_height

    # Logo remains in its own right-side area.
    logo_top = max(logo_bottom + 10, block_top - len(name_lines) * (name_size + 1) - 3)
    
    draw_image_in_box(
        c,
        company_logo,
        logo_left,
        logo_bottom,
        logo_right,
        logo_top
    )

def draw_cover_page(c, page_width, page_height, data):
    job = data.get("job") or {}
    vessel = data.get("vessel") or {}
    report = data.get("reportInfo") or {}

    # Vessel name: full page width 0 to 580, y=465, 24 size, centered.
    vessel_name = vessel.get("vesselName") or job.get("vesselName")
    vessel_text = f'"{txt(vessel_name).upper()}"' if txt(vessel_name) else ""

    if vessel_text:
        c.setFont("Times-Roman", 24)
        text_width = pdfmetrics.stringWidth(vessel_text, "Times-Roman", 24)
        x = (580 - text_width) / 2
        c.drawString(x, 465, vessel_text)

    # Company block.
    draw_company_block(c, report)

    # Client field: (350, 105, 540, 180)
    # If client logo exists, display logo. Otherwise display client name.
    client_logo = report.get("clientLogoPath")
    client_name = report.get("clientName") or job.get("clientName")

    has_logo = bool(txt(client_logo))
    has_name = bool(txt(client_name))

    if has_logo and has_name:
        draw_image_in_box(c, client_logo, 365, 125, 525, 178)

        draw_centered_text(
            c,
            350,
            105,
            540,
            125,
            txt(client_name).upper(),
            font=FONT_ARIAL,
            size=10
        )

    elif has_logo:
        draw_image_in_box(c, client_logo, 350, 105, 540, 180)

    elif has_name:
        draw_centered_text(
            c,
            350,
            105,
            540,
            180,
            txt(client_name).upper(),
            font=FONT_ARIAL,
            size=12
        )
    # Bottom table.
    draw_text(c, 135, 78, report.get("preparedBy"), font=FONT_ARIAL, size=12)
    draw_text(c, 135, 50, report.get("checkedBy"), font=FONT_ARIAL, size=12)
    draw_text(c, 135, 25, report.get("approvedBy"), font=FONT_ARIAL, size=12)

    draw_text(c, 305, 78, report.get("uid"), font=FONT_ARIAL, size=12)
    draw_text(c, 305, 50, job.get("jobNumber"), font=FONT_ARIAL, size=12)
    draw_text(c, 305, 25, report.get("documentNo"), font=FONT_ARIAL, size=12)

    # Date coordinate assumed as (500, 78, 550, 95)
    draw_centered_text(
        c,
        500,
        78,
        550,
        95,
        format_date(report.get("documentDate")),
        font=FONT_ARIAL,
        size=12
    )

    draw_centered_text(
        c,
        500,
        20,
        550,
        40,
        report.get("revisionNo"),
        font=FONT_ARIAL,
        size=12
    )


def draw_second_page(c, page_width, page_height, data):
    job = data.get("job") or {}
    vessel = data.get("vessel") or {}

    vessel_name = vessel.get("vesselName") or job.get("vesselName")
    imo_number = vessel.get("imoNumber") or job.get("imoNumber")

    vessel_text = f"VESSEL NAME : {txt(vessel_name).upper()}"
    imo_text = f"IMO NUMBER : {txt(imo_number).upper()}"

    c.setFont(FONT_TIMES, 18)

    vessel_text_width = pdfmetrics.stringWidth(vessel_text, FONT_TIMES, 18)
    vessel_x = (page_width - vessel_text_width) / 2
    c.drawString(vessel_x, 402, vessel_text)

    imo_text_width = pdfmetrics.stringWidth(imo_text, FONT_TIMES, 18)
    imo_x = (page_width - imo_text_width) / 2
    c.drawString(imo_x, 375, imo_text)


