# -*- coding: utf-8 -*-
import json
import sys
import re
from io import BytesIO
from pathlib import Path
from pypdf.annotations import Link
from pypdf.generic import Fit
from pypdf import PdfReader, PdfWriter
from pypdf._page import PageObject
from pypdf.generic import (
    ArrayObject,
    DictionaryObject,
    FloatObject,
    NameObject,
    NumberObject,
)
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.enums import TA_LEFT, TA_CENTER, TA_RIGHT
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfgen import canvas
from reportlab.platypus import Paragraph

from report_pdf.contents import make_contents_pages
from report_pdf.helpers import (
    draw_common_header_footer,
    merge_page_with_overlay_on_top,
    normalize_keys,
    register_fonts,
    replace_template_footer,
    FONT_ARIAL,
    FONT_TIMES,
    txt,
    wrap_text_to_lines,
)


SECTION_ROWS = [
    ("1", "INTRODUCTION", "INTRODUCTION"),
    ("2", "SCOPE OF WORK", "SCOPE OF WORK"),
    ("2.1", "INFORMATIVE SOURCES", "INFORMATIVE SOURCES"),
    ("3", "VESSEL PARTICULARS", "VESSEL PARTICULARS"),
    ("4", "WORK PROCEDURES", "WORK PROCEDURES"),
    (
        "5",
        "COLLECTION OF NECESSARY INFORMATION",
        "COLLECTION OF NECESSARY INFORMATION",
    ),
    (
        "6",
        "ASSESSING COLLECTED INFORMATION",
        "ASSESSING COLLECTED INFORMATION",
    ),
    (
        "6.1",
        "DEVELOPMENT OF VISUAL/SAMPLING CHECK PLAN (VSCP)",
        "DEVELOPMENT OF VISUAL/SAMPLING CHECK PLAN",
    ),
    (
        "7",
        "ONBOARD VISUAL/SAMPLING CHECK",
        "ONBOARD VISUAL/SAMPLING CHECK",
    ),
    (
        "7.1",
        "METHOD OF SURVEY",
        "METHOD OF SURVEY",
    ),
    (
        "7.2",
        "ONBOARD SURVEY",
        "ONBOARD SURVEY",
    ),
    (
        "7.3",
        "SAMPLING",
        "SAMPLING",
    ),
    (
        "7.4",
        "RISK ASSESSMENT",
        "OCCUPATIONAL HEALTH AND SAFETY RISK ASSESSMENT",
    ),
    (
        "",
        "APPENDIX 1 - RESULTS FROM THE SAMPLING CHECK / VISUAL FOR PART 1 OF THE IHM",
        "APPENDIX 1 - RESULTS FROM THE SAMPLING CHECK / VISUAL FOR PART 1 OF THE IHM",
    ),
    (
        "",
        "APPENDIX 2 - TEST RESULTS FOR HAZARDOUS MATERIALS",
        "APPENDIX 2 - TEST RESULTS FOR HAZARDOUS MATERIALS",
    ),
    (
        "",
        "APPENDIX 3 - PHOTOGRAPHS OF SAMPLING",
        "APPENDIX 3 - PHOTOGRAPHS OF SAMPLING",
    ),
    (
        "",
        "APPENDIX 4 - VISUAL / SAMPLING LOCATION MARKING PLAN",
        "APPENDIX 4 - VISUAL / SAMPLING LOCATION MARKING PLAN",
    ),
    (
        "",
        "APPENDIX 5 - VESSEL DOCUMENTS",
        "APPENDIX 5 - VESSEL DOCUMENTS",
    ),
    (
        "",
        "APPENDIX 6 - DOCUMENTS",
        "APPENDIX 6",
    ),
    (
        "",
        "APPENDIX 7 - QUALIFICATION AND CAPABILITY CERTIFICATE",
        "APPENDIX 7 - QUALIFICATION AND CAPABILITY CERTIFICATE",
    ),
]

CONTENTS_LEFT_X = 70
CONTENTS_RIGHT_X = 520
CONTENTS_TITLE_Y = 755
CONTENTS_START_Y = 720
CONTENTS_BOTTOM_Y = 60
CONTENTS_FONT_SIZE = 12


def make_style(edit):
    weight = txt(edit.get("fontWeight")).lower()
    alignment_value = txt(edit.get("alignment")).lower()

    alignment = {
        "center": TA_CENTER,
        "right": TA_RIGHT,
    }.get(alignment_value, TA_LEFT)
    font = {
        "bold": "Times-Bold",
        "italic": "Times-Italic",
    }.get(weight, "Times-Roman")

    return ParagraphStyle(
        "DynamicEdit",
        fontName=font,
        fontSize=float(edit.get("fontSize") or 12),
        leading=float(edit.get("fontSize") or 12) * 1.15,
        spaceAfter=0,
        spaceBefore=0,
        alignment=alignment,
    )


def make_paragraph(edit):
    content = txt(edit.get("content"))
    lines = content.replace("\r\n", "\n").split("\n")

    if edit.get("autoBullet"):
        content = "<br/>".join(
            f"&#8226; {line.strip()}"
            for line in lines
            if line.strip()
        )
    else:
        content = "<br/>".join(lines)

    return Paragraph(content, make_style(edit))


def make_overlay(width, height, edit, flowable):
    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(width, height))

    x = float(edit.get("x") or 0) * width
    top = float(edit.get("y") or 0) * height
    font_size = float(edit.get("fontSize") or 12)
    edit_type = txt(edit.get("editType")).lower()

    minimum_width = 120 if edit_type == "replace_text" else 20
    minimum_height = (
        max(font_size * 2, 30)
        if edit_type == "replace_text"
        else 15
    )

    box_width = max(
        float(edit.get("width") or 0.25) * width,
        minimum_width
    )

    box_height = max(
        float(edit.get("height") or 0.04) * height,
        minimum_height
    )
    y = height - top - box_height

    if edit.get("coverOriginal", True):
        c.setFillColor(colors.white)
        c.rect(x, y, box_width, box_height, stroke=0, fill=1)

    _, rendered_height = flowable.wrap(box_width, box_height)
    flowable.drawOn(c, x, y + box_height - rendered_height)

    c.save()
    packet.seek(0)
    return PdfReader(packet).pages[0]


def make_overflow_page(
    width,
    height,
    data,
    flowable,
    page_number,
    show_header_footer=True,
):
    job = data.get("job") or {}
    vessel = data.get("vessel") or {}
    report = data.get("reportInfo") or {}
    vessel_name = (
    vessel.get("vesselName")
    or job.get("vesselName")
    or ""
    )

    company_name = report.get("companyName") or ""
    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(width, height))

    if show_header_footer:
        page_width = c._pagesize[0]
        page_height = c._pagesize[1]

        left_x = 70
        right_x = page_width - 70
        top_line_y = page_height - 60
        top_text_y = page_height - 48

        c.setStrokeColor(colors.HexColor("#A0A0A0"))
        c.setLineWidth(1.5)
        c.line(left_x, top_line_y, right_x, top_line_y)

        c.setFillColor(colors.black)
        c.setFont("Times-Italic", 12)
        c.drawString(
            left_x + 6,
            top_text_y,
            "INVENTORY OF HAZARDOUS MATERIALS PART I"
        )

        vessel_text = txt(vessel_name).upper()
        vessel_width = pdfmetrics.stringWidth(
            vessel_text,
            "Times-Italic",
            12
        )

        c.drawString(
            right_x - vessel_width,
            top_text_y,
            vessel_text
        )
         # Draw footer horizontal line on newly generated pages.
        footer_line_y = 53

        c.setStrokeColor(colors.black)
        c.setLineWidth(0.5)
        c.line(
            left_x,
            footer_line_y,
            right_x,
            footer_line_y
        )

    left = 70
    bottom = 70
    available_width = width - 140
    available_height = height - 145

    _, rendered_height = flowable.wrap(
        available_width,
        available_height
    )

    flowable.drawOn(
        c,
        left,
        bottom + available_height - rendered_height,
    )

    c.save()
    packet.seek(0)
    return PdfReader(packet).pages[0]
def redraw_final_footer(page, company_name, page_number):
    return replace_template_footer(
        page,
        company_name,
        page_number
    )


def apply_text_edit(
    page,
    edit,
    data,
    show_header_footer=True,
):
    width = float(page.mediabox.width)
    height = float(page.mediabox.height)

    paragraph = make_paragraph(edit)

    font_size = float(edit.get("fontSize") or 12)
    edit_type = txt(edit.get("editType")).lower()

    minimum_width = 120 if edit_type == "replace_text" else 20
    minimum_height = (
        max(font_size * 2, 30)
        if edit_type == "replace_text"
        else 15
    )

    box_width = max(
        float(edit.get("width") or 0.25) * width,
        minimum_width
    )

    box_height = max(
        float(edit.get("height") or 0.04) * height,
        minimum_height
    )

    parts = paragraph.split(box_width, box_height)

    if not parts:
        # Ensure short replacement text is still rendered.
        overlay = make_overlay(
            width,
            height,
            edit,
            paragraph
        )

        page = merge_page_with_overlay_on_top(
            page,
            overlay
        )

        return page, []

    overlay = make_overlay(width, height, edit, parts[0])
    page = merge_page_with_overlay_on_top(page, overlay)

    overflow = []
    remaining = parts[1:]

    while remaining:
        available_width = width - 140
        available_height = height - 145

        current = remaining[0]
        page_parts = current.split(available_width, available_height)

        if page_parts:
            overflow.append(
                make_overflow_page(
                    width,
                    height,
                    data,
                    page_parts[0],
                    0,
                    show_header_footer=show_header_footer,
                )
            )
            remaining = page_parts[1:] + remaining[1:]
        else:
            break

    return page, overflow

def find_page_index(pages, keyword, start=0):
    keyword = keyword.upper()

    for index in range(start, len(pages)):
        try:
            text = (pages[index].extract_text() or "").upper()
        except Exception:
            text = ""

        if keyword in text:
            return index

    return None
def find_last_page_index(pages, keyword):
    keyword = keyword.upper()

    for index in range(len(pages) - 1, -1, -1):
        try:
            text = (pages[index].extract_text() or "").upper()
        except Exception:
            text = ""

        if keyword in text:
            return index

    return None

def build_header_footer_policy(pages, data=None):
    page_texts = extract_normalized_page_texts(pages)

    def find_heading(keyword, start=1):
        return find_heading_from(page_texts, keyword, start)

    ohs_page_indexes = [
    index
    for index, text in enumerate(page_texts)
    if (
        "OCCUPATIONAL HEALTH AND SAFETY" in text
        and "RISK ASSESSMENT" in text
    )
    ]

    ohs_first = min(ohs_page_indexes) if ohs_page_indexes else None
    ohs_last = max(ohs_page_indexes) if ohs_page_indexes else None
    search_from = (
        ohs_last + 1
        if ohs_last is not None
        else 1
    )
    appendix_one = find_heading(
        "APPENDIX 1 - RESULTS FROM THE SAMPLING CHECK",
        search_from
    )
    appendix_two = find_heading(
        "APPENDIX 2 - TEST RESULTS FOR HAZARDOUS MATERIALS",
        appendix_one or search_from
    )
    appendix_three = find_heading(
        "APPENDIX 3 - PHOTOGRAPHS OF SAMPLING",
        appendix_two or search_from
    )
    appendix_four = next(
    (
        index
        for index in range(len(page_texts) - 1, -1, -1)
        if (
            "APPENDIX 4" in page_texts[index]
            and "VISUAL / SAMPLING LOCATION" in page_texts[index]
        )
    ),
    None,
    )
    appendix_five = find_heading(
        "APPENDIX 5 - VESSEL DOCUMENTS",
        appendix_four or search_from
    )
    appendix_six = find_heading(
        "APPENDIX 6",
        appendix_five or search_from
    )
    appendix_seven = find_heading(
        "APPENDIX 7 - QUALIFICATION AND CAPABILITY CERTIFICATE",
        appendix_six or search_from
    )

    custom_heading_indexes = []

    custom_appendices = (data or {}).get("customAppendices") or []
    custom_titles = {}

    for item in custom_appendices:
        appendix_no = item.get("appendixNo")
        if appendix_no is None:
            continue

        appendix_no = int(appendix_no)
        custom_titles[appendix_no] = (
            item.get("appendixTitle")
            or "ADDITIONAL DOCUMENTS"
        )

    custom_search_from = appendix_seven or search_from

    for appendix_no in sorted(custom_titles):
        title = custom_titles[appendix_no]

        heading_index = find_heading(
            f"APPENDIX {appendix_no} - {title}",
            custom_search_from
        )

        if heading_index is None:
            heading_index = find_heading(
                f"APPENDIX {appendix_no}",
                custom_search_from
            )

        if heading_index is not None:
            custom_heading_indexes.append(heading_index)
            custom_search_from = heading_index + 1

    def between(index, start, end):
        return (
            start is not None
            and end is not None
            and start < index < end
        )

    def should_show(page_index):
        # Cover page.
        if page_index == 0:
            return False

        # Newly generated OHS page and following three pages.
        # All dynamically generated OHS pages and next 3 template pages.
        if (
            ohs_first is not None
            and ohs_last is not None
            and ohs_first <= page_index <= ohs_last + 3
        ):
            return False

        # Appendix 1 pages retain footer.
        if between(page_index, appendix_one, appendix_two):
            return True

        # Appendix 2 heading has footer; uploaded files do not.
        if appendix_two is not None and page_index == appendix_two:
            return True

        if between(page_index, appendix_two, appendix_three):
            return False

        # Appendix 3 heading and generated photo pages have footer.
        if (
            appendix_three is not None
            and appendix_four is not None
            and appendix_three <= page_index < appendix_four
        ):
            return True

        # Appendix 4 heading has footer.
        if appendix_four is not None:
            if page_index == appendix_four:
                return True

            if page_index == appendix_four + 1:
                return False

            if appendix_four + 2 <= page_index <= appendix_four + 4:
                return True

        # Appendix 5 bullet page has footer; uploaded files do not.
        if (
            appendix_five is not None
            and appendix_six is not None
            and appendix_five <= page_index < appendix_six
        ):
            # Bullet pages contain this marker. Uploaded pages do not.
            return "IHM_APPENDIX_5_GENERATED" in page_texts[page_index]

        if between(page_index, appendix_five, appendix_six):
            return False

        # Appendix 6 heading and next page have footer.
        if appendix_six is not None:
            if page_index in (appendix_six, appendix_six + 1):
                return True

            if (
                appendix_seven is not None
                and appendix_six + 1 < page_index < appendix_seven
            ):
                return False

        # Appendix 7 heading has footer; uploaded files do not.
        if appendix_seven is not None and page_index == appendix_seven:
            return True

        first_custom = (
            custom_heading_indexes[0]
            if custom_heading_indexes
            else None
        )

        if between(page_index, appendix_seven, first_custom):
            return False

        # More appendix heading page has footer.
        # Pages between headings are uploaded files: no footer.
        for position, heading_index in enumerate(custom_heading_indexes):
            if page_index == heading_index:
                return True

            next_heading = (
                custom_heading_indexes[position + 1]
                if position + 1 < len(custom_heading_indexes)
                else len(pages)
            )

            if heading_index < page_index < next_heading:
                return False

        return True

    return should_show
def normalize_page_text(value):
    text = (value or "").upper()

    # Use chr() so the source file remains ASCII-safe.
    text = text.replace(chr(0x2013), "-")
    text = text.replace(chr(0x2014), "-")
    text = text.replace(chr(0x00A0), " ")

    text = re.sub(r"\s*-\s*", " - ", text)
    text = re.sub(r"\s+", " ", text)

    return text.strip()

def extract_normalized_page_texts(pages):
    texts = []

    for page in pages:
        try:
            texts.append(
                normalize_page_text(page.extract_text() or "")
            )
        except Exception:
            texts.append("")

    return texts


def find_heading_from(
    page_texts,
    keyword,
    start_index,
):
    normalized_keyword = normalize_page_text(keyword)

    for index in range(
        max(start_index, 0),
        len(page_texts)
    ):
        if normalized_keyword in page_texts[index]:
            return index

    return None


def build_sequential_contents_rows(
    pages,
    data,
    page_texts=None,
    search_start_index=None,
):
    """
    Find the real section-heading pages sequentially.

    search_start_index must point after the generated Contents pages and
    Revision History page. Otherwise, section names printed inside the
    Contents page itself will be mistaken for actual destination pages.
    """

    if page_texts is None:
        page_texts = extract_normalized_page_texts(pages)

    rows = []

    if search_start_index is not None:
        search_from = max(int(search_start_index), 0)
    else:
        contents_index = next(
            (
                index
                for index, text in enumerate(page_texts)
                if "CONTENTS" in text
            ),
            None,
        )

        if contents_index is None:
            search_from = 1
        else:
            # Search for Revision History after the Contents page.
            revision_index = find_heading_from(
                page_texts,
                "REVISION HISTORY",
                contents_index + 1,
            )

            if revision_index is not None:
                search_from = revision_index + 1
            else:
                # Fallback: skip every consecutive page that looks like
                # a generated Contents page.
                search_from = contents_index + 1

                while search_from < len(page_texts):
                    current_text = page_texts[search_from]

                    contents_keyword_count = sum(
                        1
                        for _, title, _ in SECTION_ROWS
                        if normalize_page_text(title) in current_text
                    )

                    # A contents continuation page normally contains several
                    # section or appendix titles.
                    if (
                        "CONTENTS" in current_text
                        or contents_keyword_count >= 3
                    ):
                        search_from += 1
                        continue

                    break

    definitions = list(SECTION_ROWS)

    custom_appendices = data.get("customAppendices") or []
    custom_groups = {}

    for item in custom_appendices:
        appendix_no = item.get("appendixNo")

        if appendix_no is None:
            continue

        try:
            appendix_no = int(appendix_no)
        except (TypeError, ValueError):
            continue

        title = (
            item.get("appendixTitle")
            or "ADDITIONAL DOCUMENTS"
        )

        custom_groups[appendix_no] = title

    for appendix_no in sorted(custom_groups):
        title = custom_groups[appendix_no]
        full_heading = (
            f"APPENDIX {appendix_no} - {title}"
        )

        definitions.append((
            "",
            full_heading,
            full_heading,
        ))

    for number, title, keyword in definitions:
        found_index = find_heading_from(
            page_texts,
            keyword,
            search_from,
        )

        # Appendix text extraction may split or normalize the full heading.
        if found_index is None:
            appendix_match = re.search(
                r"APPENDIX\s+(\d+)",
                normalize_page_text(title),
            )

            if appendix_match:
                appendix_number = appendix_match.group(1)

                found_index = find_heading_from(
                    page_texts,
                    f"APPENDIX {appendix_number}",
                    search_from,
                )

        if (
            found_index is None
            and normalize_page_text(title) == "RISK ASSESSMENT"
        ):
            found_index = find_heading_from(
                page_texts,
                "RISK ASSESSMENT",
                search_from,
            )

        rows.append({
            "number": number,
            "title": title,
            "pageNo": (
                found_index
                if found_index is not None
                else "-"
            ),
        })

        # Start the next search after the current heading page.
        # Using +1 prevents two different rows from resolving to the
        # same page because of overlapping heading text.
        if found_index is not None:
            search_from = found_index + 1

    return rows

def update_cover_total(page, total_pages):
    width = float(page.mediabox.width)
    height = float(page.mediabox.height)

    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(width, height))

    # Clear old total-page value.
    c.setFillColor(colors.white)
    c.rect(498, 48, 55, 20, stroke=0, fill=1)

    text = str(total_pages)
    c.setFillColor(colors.black)
    c.setFont(FONT_ARIAL, 12)

    text_width = pdfmetrics.stringWidth(
        text,
        FONT_ARIAL,
        12
    )

    x = 500 + (50 - text_width) / 2
    c.drawString(x, 53, text)

    c.save()
    packet.seek(0)

    overlay = PdfReader(packet).pages[0]
    return merge_page_with_overlay_on_top(page, overlay)

def rebuild_contents(pages, data):
    if not pages:
        return pages

    page_texts = extract_normalized_page_texts(pages)

    contents_index = next(
        (
            index
            for index, text in enumerate(page_texts)
            if "CONTENTS" in text
        ),
        None,
    )

    if contents_index is None:
        return pages

    introduction_index = find_heading_from(
        page_texts,
        "INTRODUCTION",
        contents_index + 1,
    )

    if introduction_index is None:
        return pages

    revision_index = find_heading_from(
        page_texts,
        "REVISION HISTORY",
        contents_index + 1,
    )

    # Remove only the existing Contents pages.
    # Preserve the Revision History page.
    contents_end_index = (
        revision_index
        if (
            revision_index is not None
            and revision_index < introduction_index
        )
        else introduction_index
    )

    pages_without_contents = (
        pages[:contents_index]
        + pages[contents_end_index:]
    )

    page_texts_without_contents = (
        page_texts[:contents_index]
        + page_texts[contents_end_index:]
    )

    contents_count = 1
    generated = []

    for _ in range(5):
        placeholders = [
            PageObject.create_blank_page(
                width=float(pages[0].mediabox.width),
                height=float(pages[0].mediabox.height),
            )
            for _ in range(contents_count)
        ]

        shifted_pages = (
            pages_without_contents[:contents_index]
            + placeholders
            + pages_without_contents[contents_index:]
        )

        shifted_page_texts = (
            page_texts_without_contents[:contents_index]
            + [""] * contents_count
            + page_texts_without_contents[contents_index:]
        )

        # Actual headings begin only after the inserted Contents pages.
        search_start_index = (
            contents_index + contents_count
        )

        rows = build_sequential_contents_rows(
            shifted_pages,
            data,
            page_texts=shifted_page_texts,
            search_start_index=search_start_index,
        )

        generated = make_contents_pages(
            float(pages[0].mediabox.width),
            float(pages[0].mediabox.height),
            data,
            rows,
        )

        new_count = len(generated)

        if new_count == contents_count:
            break

        contents_count = new_count

    return (
        pages_without_contents[:contents_index]
        + generated
        + pages_without_contents[contents_index:]
    )
def _content_row_bottom_y(y, number, title, page_no):
    number = txt(number)
    title = txt(title).upper()
    page_text = txt(page_no)
    page_x = CONTENTS_RIGHT_X - pdfmetrics.stringWidth(
        page_text,
        FONT_TIMES,
        CONTENTS_FONT_SIZE,
    )
    title_x = CONTENTS_LEFT_X + 20 if number else CONTENTS_LEFT_X
    max_title_width = page_x - title_x - 18
    title_lines = wrap_text_to_lines(
        title,
        FONT_TIMES,
        CONTENTS_FONT_SIZE,
        max_title_width,
    )

    return y - (max(len(title_lines), 1) * 16) - 8


def collect_contents_links(pages, data):
    """
    Collect clickable Contents links using actual section pages.

    The section search starts after the Contents and Revision History
    pages so titles printed inside the Contents page are never selected
    as destinations.
    """

    if not pages:
        return []

    page_texts = extract_normalized_page_texts(pages)

    contents_index = next(
        (
            index
            for index, text in enumerate(page_texts)
            if "CONTENTS" in text
        ),
        None,
    )

    if contents_index is None:
        return []

    revision_index = find_heading_from(
        page_texts,
        "REVISION HISTORY",
        contents_index + 1,
    )

    if revision_index is not None:
        search_start_index = revision_index + 1
    else:
        # Find the first page after all generated Contents pages.
        search_start_index = contents_index + 1

        while search_start_index < len(page_texts):
            current_text = page_texts[search_start_index]

            contents_keyword_count = sum(
                1
                for _, title, _ in SECTION_ROWS
                if normalize_page_text(title) in current_text
            )

            if (
                "CONTENTS" in current_text
                or contents_keyword_count >= 3
            ):
                search_start_index += 1
                continue

            break

    rows = build_sequential_contents_rows(
        pages,
        data,
        page_texts=page_texts,
        search_start_index=search_start_index,
    )

    links = []
    contents_page_index = contents_index
    y = CONTENTS_START_Y

    for row in rows:
        if y <= CONTENTS_BOTTOM_Y:
            contents_page_index += 1
            y = CONTENTS_TITLE_Y

        next_y = _content_row_bottom_y(
            y,
            row.get("number"),
            row.get("title"),
            row.get("pageNo"),
        )

        target_page_index = row.get("pageNo")

        if (
            isinstance(target_page_index, int)
            and 0 <= target_page_index < len(pages)
            and target_page_index != contents_page_index
        ):
            links.append({
                "contentsPage": contents_page_index,
                "targetPage": target_page_index,
                "rect": [
                    CONTENTS_LEFT_X,
                    max(next_y + 4, 0),
                    CONTENTS_RIGHT_X,
                    y + 14,
                ],
            })

        y = next_y

    return links


def add_contents_links(writer, links):
    """
    Add internal clickable links to the contents pages.

    Uses pypdf's native Link annotation so that destination page
    references are resolved against the final PdfWriter page tree.
    """

    total_pages = len(writer.pages)

    for link in links:
        try:
            contents_page_index = int(
                link.get("contentsPage")
            )
            target_page_index = int(
                link.get("targetPage")
            )
            rect_values = link.get("rect") or []
        except (TypeError, ValueError, AttributeError):
            continue

        # Validate source page.
        if not 0 <= contents_page_index < total_pages:
            continue

        # Validate destination page.
        if not 0 <= target_page_index < total_pages:
            continue

        # PDF link rectangle must contain:
        # [left, bottom, right, top]
        if len(rect_values) != 4:
            continue

        try:
            left = float(rect_values[0])
            bottom = float(rect_values[1])
            right = float(rect_values[2])
            top = float(rect_values[3])
        except (TypeError, ValueError):
            continue

        # Reject invalid rectangles.
        if right <= left or top <= bottom:
            continue

        link_annotation = Link(
            rect=(
                left,
                bottom,
                right,
                top,
            ),
            target_page_index=target_page_index,
            fit=Fit.fit(),
            border=[
                0,
                0,
                0,
            ],
        )

        writer.add_annotation(
            page_number=contents_page_index,
            annotation=link_annotation,
        )

def main():
    if len(sys.argv) < 5:
        raise SystemExit(
            "Usage: apply_pdf_dynamic_edits.py "
            "input.pdf edits.json report-input.json output.pdf"
        )

    register_fonts()

    input_pdf = Path(sys.argv[1])
    edits_file = Path(sys.argv[2])
    report_input = Path(sys.argv[3])
    output_pdf = Path(sys.argv[4])

    edits = normalize_keys(
        json.loads(edits_file.read_text(encoding="utf-8"))
    )
    data = normalize_keys(
        json.loads(report_input.read_text(encoding="utf-8"))
    )

    reader = PdfReader(str(input_pdf))
    original_pages = list(reader.pages)
    original_header_footer_policy = build_header_footer_policy(
    original_pages,data
)

    removed = {
        int(edit.get("pageNumber"))
        for edit in edits
        if edit.get("editType") == "remove_page"
    }

    edits_by_page = {}

    for edit in edits:
        if edit.get("editType") == "remove_page":
            continue

        edits_by_page.setdefault(
            int(edit.get("pageNumber") or 0),
            []
        ).append(edit)

    pages = []
    structural_changed = bool(removed)
    for original_number, source_page in enumerate(
        original_pages,
        start=1
    ):
        if original_number in removed:
            continue

        page = source_page
        overflow_pages = []

        show_header_footer = original_header_footer_policy(
            original_number - 1
        )

        for edit in edits_by_page.get(original_number, []):
            page, overflow = apply_text_edit(
                page,
                edit,
                data,
                show_header_footer=show_header_footer,
            )
            overflow_pages.extend(overflow)

        pages.append(page)
        pages.extend(overflow_pages)

        if overflow_pages:
            structural_changed = True

    if structural_changed:
        pages = rebuild_contents(pages, data)

    company_name = (
        data.get("reportInfo") or {}
    ).get("companyName")

    final_header_footer_policy = build_header_footer_policy(pages, data)
    renumbered = []
    visible_page_number = 0

    for index, page in enumerate(pages):
        # Cover page: do not display or count as page 1.
        if index == 0:
            renumbered.append(page)
            continue

        if final_header_footer_policy(index):
            visible_page_number += 1
            page = redraw_final_footer(
                page,
                company_name,
                visible_page_number,
            )

        renumbered.append(page)

    # Cover is not included in displayed page count.
        # Cover is not included in displayed page count.
    if renumbered:
        renumbered[0] = update_cover_total(
        renumbered[0],
        max(len(renumbered) - 1, 0)
    )

    contents_links = collect_contents_links(renumbered, data) if structural_changed else []
    writer = PdfWriter()

    for page in renumbered:
        writer.add_page(page)

    add_contents_links(writer, contents_links)

    output_pdf.parent.mkdir(parents=True, exist_ok=True)

    with output_pdf.open("wb") as stream:
        writer.write(stream)


if __name__ == "__main__":
    main()
