import json
import sys
import traceback
from datetime import datetime
from io import BytesIO
from pathlib import Path
from reportlab.pdfbase.ttfonts import TTFont
from pypdf import PdfReader, PdfWriter
from reportlab.lib import colors
from reportlab.lib.utils import ImageReader
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfgen import canvas
from pypdf._page import PageObject
from report_pdf.collection_information import make_collection_information_pages
from report_pdf.hazardous_materials import make_hazardous_materials_a3_page

from report_pdf.helpers import (normalize_keys,register_fonts,extract_page_texts,find_page_by_text_after,merge_page_with_overlay_on_top,replace_template_header_vessel_name,replace_template_footer,FONT_ARIAL,)
from report_pdf.visual_sampling_check_plan import (
    make_visual_sampling_check_plan_pages,
    make_onboard_visual_sampling_overlay,
)
from report_pdf.cover import draw_cover_page, draw_second_page
from report_pdf.revision import (draw_revision_page,build_revision_display_rows,get_first_revision_page_next_index,make_blank_page_with_revision_rows,)
from report_pdf.vessel_particulars import (
    draw_vessel_particulars_page,
    build_vessel_particular_rows,
    draw_vessel_particular_table_page_count_index,
    make_blank_page_with_vessel_rows,
)
from report_pdf.introduction import (
    draw_introduction_page,
    draw_lab_report_reference_page,
)
from report_pdf.ohs_risk_assessment import make_ohs_risk_assessment_pages
from report_pdf.appendix_one_results import make_appendix_one_results_pages
from report_pdf.appendix_two_attachments import make_appendix_two_attachment_pages
from report_pdf.appendix_three_sampling_photos import make_appendix_three_sampling_photo_pages
from report_pdf.appendix_five_vessel_documents import make_appendix_five_vessel_document_pages,_make_bullet_pages
from report_pdf.appendix_seven_certificates import make_appendix_seven_certificate_pages
from report_pdf.custom_appendices import make_custom_appendix_pages
from report_pdf.contents import make_contents_pages
from report_pdf.appendix_four_layout import make_appendix_four_location_plan_pages

def create_overlay(page_width, page_height, page_index, data, revision_page_index=None, introduction_page_index=None, vessel_particulars_page_index=None):
    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    if page_index == 0:
        draw_cover_page(c, page_width, page_height, data)
    elif page_index == 1:
        draw_second_page(c, page_width, page_height, data)
    elif revision_page_index is not None and page_index == revision_page_index:
        draw_revision_page(
            c,
            page_width,
            page_height,
            data,
            start_row_index=0,
            first_template_page=True
        )
    elif introduction_page_index is not None and page_index == introduction_page_index:
     
        draw_introduction_page(c, page_width, page_height, data)
    elif introduction_page_index is not None and page_index == introduction_page_index + 1:

        draw_lab_report_reference_page(c, page_width, page_height, data)

    elif vessel_particulars_page_index is not None and page_index == vessel_particulars_page_index:
  
        draw_vessel_particulars_page(
            c,
            page_width,
            page_height,
            data,
            start_row_index=0,
            first_template_page=True
        )
    c.save()
    packet.seek(0)
    return PdfReader(packet)

def should_update_template_header(
    page_index,
    see_below_attachment_page_index=None,
    appendix_six_page_index=None,
    appendix_seven_page_index=None,
):
    if page_index == 0:
        return False

    if (
        see_below_attachment_page_index is not None
        and see_below_attachment_page_index < page_index <= see_below_attachment_page_index + 6
    ):
        return False

    if appendix_seven_page_index is not None and page_index == appendix_seven_page_index:
        return True

    if appendix_six_page_index is not None:
        if page_index == appendix_six_page_index + 1:
            return True

        if page_index > appendix_six_page_index:
            return False

    return True

def add_total_page_count_to_cover(writer, total_pages):
    if len(writer.pages) == 0:
        return writer

    old_pages = list(writer.pages)
    new_writer = PdfWriter()

    first_page = old_pages[0]
    page_width = float(first_page.mediabox.width)
    page_height = float(first_page.mediabox.height)

    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    c.setFont(FONT_ARIAL, 12)

    text = str(total_pages)
    text_width = pdfmetrics.stringWidth(text, FONT_ARIAL, 12)

    x1 = 500
    x2 = 550
    y1 = 50
    y2 = 65

    x = x1 + ((x2 - x1) - text_width) / 2
    y = y1 + ((y2 - y1) - 12) / 2 + 2

    c.drawString(x, y, text)

    c.save()
    packet.seek(0)

    overlay = PdfReader(packet).pages[0]
    first_page = merge_page_with_overlay_on_top(first_page, overlay)

    new_writer.add_page(first_page)

    for page in old_pages[1:]:
        new_writer.add_page(page)

    return new_writer

def replace_contents_placeholder(writer, footer_flags, insert_index, contents_pages):
    old_pages = list(writer.pages)
    new_writer = PdfWriter()
    new_footer_flags = []

    for index, page in enumerate(old_pages):
        if index == insert_index:
            for contents_page in contents_pages:
                new_writer.add_page(contents_page)
                new_footer_flags.append(True)
        else:
            new_writer.add_page(page)
            new_footer_flags.append(
                footer_flags[index] if index < len(footer_flags) else False
            )

    return new_writer, new_footer_flags

def renumber_visible_footers(writer, data, footer_flags):
    old_pages = list(writer.pages)
    new_writer = PdfWriter()
    company_name = get_company_name(data)

    for index, page in enumerate(old_pages):
        # Cover page is excluded from page numbering.
        if index == 0:
            new_writer.add_page(page)
            continue

        page_no = index
        show_footer = (
            index < len(footer_flags)
            and footer_flags[index]
        )

        if show_footer:
            page = replace_template_footer(
                page,
                company_name,
                page_no
            )

        new_writer.add_page(page)

    return new_writer, max(len(old_pages) - 1, 0)

def update_template_header(
    page,
    data,
    page_index,
    see_below_attachment_page_index=None,
    appendix_six_page_index=None,
    appendix_seven_page_index=None,
):
    if not should_update_template_header(
        page_index,
        see_below_attachment_page_index,
        appendix_six_page_index,
        appendix_seven_page_index,
    ):
        return page

    job = data.get("job") or {}
    vessel = data.get("vessel") or {}

    vessel_name = vessel.get("vesselName") or job.get("vesselName")

    return replace_template_header_vessel_name(page, vessel_name)

def get_company_name(data):
    report = data.get("reportInfo") or {}
    return report.get("companyName")


def update_template_footer(
    page,
    data,
    page_index,
    display_page_number,
    see_below_attachment_page_index=None,
    appendix_six_page_index=None,
    appendix_seven_page_index=None,
):
    if page_index == 0:
        return page

    if not should_update_template_header(
        page_index,
        see_below_attachment_page_index,
        appendix_six_page_index,
        appendix_seven_page_index,
    ):
        return page

    return replace_template_footer(
        page,
        get_company_name(data),
        display_page_number
    )

def main():
    if len(sys.argv) < 3:
        print("Usage: generate_ihm_report.py input.json output.pdf", file=sys.stderr)
        sys.exit(1)

    register_fonts()
    input_json = Path(sys.argv[1])
    output_pdf = Path(sys.argv[2])

    log_path = output_pdf.parent / "generator.log"

    def log_stage(message):
        output_pdf.parent.mkdir(parents=True, exist_ok=True)
        line = f"{datetime.now():%Y-%m-%d %H:%M:%S} | {message}"
        print(line, flush=True)

        with log_path.open("a", encoding="utf-8") as log_file:
            log_file.write(line + "\n")
            log_file.flush()

    log_path.write_text("", encoding="utf-8")
    log_stage(f"START IHM PDF generation. input={input_json}, output={output_pdf}")

    raw_data = json.loads(input_json.read_text(encoding="utf-8"))
    data = normalize_keys(raw_data)
    log_stage("Loaded input JSON and normalized keys")
    job = data.get("job") or {}
    job_id = job.get("id") or data.get("jobId") or data.get("id")

    base_dir = Path(__file__).resolve().parent
    template_pdf = base_dir / "templates" / "reference.pdf"

    if not template_pdf.exists():
        print(f"Reference PDF not found: {template_pdf}", file=sys.stderr)
        sys.exit(1)

    template_reader = PdfReader(str(template_pdf))
    log_stage(f"Loaded template PDF. template_pages={len(template_reader.pages)}")
    text_cache_path = template_pdf.with_suffix(".text-cache.json")
    template_stat = template_pdf.stat()
    template_key = f"{template_stat.st_size}:{template_stat.st_mtime_ns}"

    page_texts = None

    if text_cache_path.exists():
        try:
            cache_data = json.loads(
                text_cache_path.read_text(encoding="utf-8")
            )

            if cache_data.get("templateKey") == template_key:
                page_texts = cache_data.get("pageTexts")
        except Exception:
            page_texts = None

    if not isinstance(page_texts, list):
        log_stage("Extracting template page texts")
        page_texts = extract_page_texts(template_reader)

        text_cache_path.write_text(
            json.dumps({
                "templateKey": template_key,
                "pageTexts": page_texts
            }),
            encoding="utf-8"
        )
        log_stage("Template page text extraction completed and cache written")
    contents_page_index = find_page_by_text_after(
        page_texts,
        "CONTENTS",
        start_page=1
    )
    revision_page_index = find_page_by_text_after(page_texts, "REVISION HISTORY", start_page=2)
    
    introduction_search_start = revision_page_index if revision_page_index is not None else 2
    introduction_page_index = find_page_by_text_after(
        page_texts,
        "INTRODUCTION",
        start_page=introduction_search_start
    )

    scope_search_start = introduction_page_index if introduction_page_index is not None else introduction_search_start
    scope_of_work_page_index = find_page_by_text_after(
        page_texts,
        "SCOPE OF WORK",
        start_page=scope_search_start
    )

    informative_search_start = scope_of_work_page_index if scope_of_work_page_index is not None else scope_search_start
    informative_sources_page_index = find_page_by_text_after(
        page_texts,
        "INFORMATIVE SOURCES",
        start_page=informative_search_start
    )

    vessel_search_start = informative_sources_page_index if informative_sources_page_index is not None else informative_search_start
    vessel_particulars_page_index = find_page_by_text_after(
        page_texts,
        "VESSEL PARTICULARS",
        start_page=vessel_search_start
    )

    work_search_start = vessel_particulars_page_index if vessel_particulars_page_index is not None else vessel_search_start
    work_procedures_page_index = find_page_by_text_after(
        page_texts,
        "WORK PROCEDURES",
        start_page=work_search_start
    )

    hazardous_search_start = work_procedures_page_index if work_procedures_page_index is not None else work_search_start
    hazardous_materials_page_index = find_page_by_text_after(
            page_texts,
        "HAZARDOUS MATERIALS CONTAINED",
        start_page=hazardous_search_start
    )
    collection_search_start = hazardous_materials_page_index if hazardous_materials_page_index is not None else hazardous_search_start
    collection_information_page_index = find_page_by_text_after(
        page_texts,
        "COLLECTION OF NECESSARY INFORMATION",
        start_page=collection_search_start
    )

    assessing_search_start = collection_information_page_index if collection_information_page_index is not None else collection_search_start
    assessing_collected_information_page_index = find_page_by_text_after(
        page_texts,
        "ASSESSING COLLECTED INFORMATION",
        start_page=assessing_search_start
    )

    visual_sampling_search_start = (
        assessing_collected_information_page_index
        if assessing_collected_information_page_index is not None
        else assessing_search_start
    )
   
    visual_sampling_check_plan_page_index = find_page_by_text_after(
        page_texts,
        "CHEM VI LABORATORY SDN BHD",
        start_page=visual_sampling_search_start
    )
    onboard_visual_sampling_search_start = (
        visual_sampling_check_plan_page_index + 1
        if visual_sampling_check_plan_page_index is not None
        else visual_sampling_search_start
    )

    onboard_visual_sampling_page_index = find_page_by_text_after(
        page_texts,
        "ONBOARD VISUAL/SAMPLING CHECK",
        start_page=onboard_visual_sampling_search_start
    )
    see_below_search_start = (
    onboard_visual_sampling_page_index + 1
    if onboard_visual_sampling_page_index is not None
    else 2
    )

    see_below_attachment_page_index = find_page_by_text_after(
        page_texts,
        "See below attachment",
        start_page=see_below_search_start
    )
    appendix_one_search_start = (
    see_below_attachment_page_index + 1
    if see_below_attachment_page_index is not None
    else 2
    )

    appendix_one_page_index = find_page_by_text_after(
        page_texts,
        "APPENDIX 1",
        start_page=appendix_one_search_start
    )
    appendix_two_search_start = (
    appendix_one_page_index + 1
    if appendix_one_page_index is not None
    else 2
    )

    appendix_two_page_index = find_page_by_text_after(
        page_texts,
        "APPENDIX 2",
        start_page=appendix_two_search_start
    )
    appendix_three_search_start = (
    appendix_two_page_index + 1
    if appendix_two_page_index is not None
    else 2
    )

    appendix_three_page_index = find_page_by_text_after(
        page_texts,
        "APPENDIX 3",
        start_page=appendix_three_search_start
    )
    appendix_four_search_start = (
    appendix_three_page_index + 75
    if appendix_three_page_index is not None
    else 2
    )

    appendix_four_page_index = find_page_by_text_after(
        page_texts,
        "APPENDIX 4",
        start_page=appendix_four_search_start
    )
    appendix_five_search_start = (
    appendix_three_page_index + 1
    if appendix_three_page_index is not None
    else 2
    )

    appendix_five_page_index = find_page_by_text_after(
        page_texts,
        "APPENDIX 5",
        start_page=appendix_five_search_start
    )
    appendix_six_search_start = (
    appendix_five_page_index + 1
    if appendix_five_page_index is not None
    else 2
    )

    appendix_six_page_index = find_page_by_text_after(
        page_texts,
        "APPENDIX 6",
        start_page=appendix_six_search_start
    )
    appendix_seven_search_start = (
    appendix_six_page_index + 1
    if appendix_six_page_index is not None
    else (
        appendix_five_page_index + 1
        if appendix_five_page_index is not None
        else 2
    )
    )

    appendix_seven_page_index = find_page_by_text_after(
        page_texts,
        "APPENDIX 7",
        start_page=appendix_seven_search_start
    )
    writer = PdfWriter()
    log_stage("Started building PDF pages")
    footer_flags = []
    display_page_number = 0

    section_pages = {}
    contents_insert_index = None
    contents_page_width = None
    contents_page_height = None

    def add_page(page, template_page_index=None, update_footer=False, generated_footer=False):
        nonlocal display_page_number

        should_show_footer = False

        if update_footer and template_page_index is not None:
            should_show_footer = (
                template_page_index != 0
                and should_update_template_header(
                    template_page_index,
                    see_below_attachment_page_index,
                    appendix_six_page_index,
                    appendix_seven_page_index
                )
            )

        if generated_footer:
            should_show_footer = True

        writer.add_page(page)
        footer_flags.append(should_show_footer)

    def mark_section(key):
        if key not in section_pages:
            section_pages[key] = len(writer.pages)

    revision_next_index = None
    skip_template_pages_after_hazardous = 0
    for page_index, page in enumerate(template_reader.pages):
        if page_index % 25 == 0:
            log_stage(f"Processing template page index={page_index}")
        if skip_template_pages_after_hazardous > 0:
            skip_template_pages_after_hazardous -= 1
            continue
        page_width = float(page.mediabox.width)
        page_height = float(page.mediabox.height)

        if contents_page_index is not None and page_index == contents_page_index:
            contents_insert_index = len(writer.pages)
            contents_page_width = page_width
            contents_page_height = page_height

            placeholder_page = PageObject.create_blank_page(
                width=page_width,
                height=page_height
            )

            add_page(
                placeholder_page,
                template_page_index=contents_page_index,
                update_footer=False
            )
            continue

        if hazardous_materials_page_index is not None and page_index == hazardous_materials_page_index:
            mark_section("hazardousMaterials")
            log_stage("Generating hazardous materials pages")
            hazardous_pages = make_hazardous_materials_a3_page(data)

            if isinstance(hazardous_pages, list):
                for hazardous_page in hazardous_pages:
                    add_page(hazardous_page, generated_footer=True)
            else:
                add_page(hazardous_pages, generated_footer=True)

            skip_template_pages_after_hazardous = 3
            continue
        if collection_information_page_index is not None and page_index == collection_information_page_index:
            mark_section("collectionInformation")
            log_stage("Generating collection information pages")
            collection_pages = make_collection_information_pages(page_width, page_height, data)

            if collection_pages:
                page = update_template_header(
                        page,
                        data,
                        page_index,
                        see_below_attachment_page_index,
                        appendix_six_page_index,
                        appendix_seven_page_index
                    )
                page = merge_page_with_overlay_on_top(page, collection_pages[0])
                add_page(page, page_index, update_footer=True)

                for extra_page in collection_pages[1:]:
                    add_page(extra_page, generated_footer=True)

                continue
        if (
            assessing_collected_information_page_index is not None
            and page_index == assessing_collected_information_page_index
        ):
            mark_section("assessingCollectedInformation")

        if visual_sampling_check_plan_page_index is not None and page_index == visual_sampling_check_plan_page_index:
            mark_section("visualSamplingPlan")
            log_stage("Generating visual sampling check plan pages")
            visual_pages = make_visual_sampling_check_plan_pages(page_width, page_height, data)

            if visual_pages:
                page = update_template_header(
                        page,
                        data,
                        page_index,
                        see_below_attachment_page_index,
                        appendix_six_page_index,
                        appendix_seven_page_index
                    )
                page = merge_page_with_overlay_on_top(page, visual_pages[0])
                add_page(page, page_index, update_footer=True)

                for extra_page in visual_pages[1:]:
                    add_page(extra_page, generated_footer=True)

                continue

        if onboard_visual_sampling_page_index is not None and page_index == onboard_visual_sampling_page_index:
            mark_section("onboardVisualSampling")
            overlay_page = make_onboard_visual_sampling_overlay(page_width, page_height, data)

            if overlay_page:
                page = merge_page_with_overlay_on_top(page, overlay_page)

            page = update_template_header(
                    page,
                    data,
                    page_index,
                    see_below_attachment_page_index,
                    appendix_six_page_index,
                    appendix_seven_page_index
                )
            add_page(page, page_index, update_footer=True)
            continue
        if (
            see_below_attachment_page_index is not None
            and page_index == see_below_attachment_page_index
        ):
            mark_section("riskAssessment")
            page = update_template_header(
                    page,
                    data,
                    page_index,
                    see_below_attachment_page_index,
                    appendix_six_page_index,
                    appendix_seven_page_index
                )
            add_page(page, page_index, update_footer=True)
            log_stage("Generating OHS risk assessment pages")
            for ohs_page in make_ohs_risk_assessment_pages(data):
                add_page(ohs_page)
            continue

        if (see_below_attachment_page_index is not None and see_below_attachment_page_index < page_index <= see_below_attachment_page_index + 3):
            continue

        if appendix_one_page_index is not None and page_index == appendix_one_page_index:
            mark_section("appendixOne")
            log_stage("Generating Appendix 1 pages")
            appendix_pages = make_appendix_one_results_pages(page_width, page_height, data)

            if appendix_pages:
                page = update_template_header(
                        page,
                        data,
                        page_index,
                        see_below_attachment_page_index,
                        appendix_six_page_index,
                        appendix_seven_page_index
                    )
                page = merge_page_with_overlay_on_top(page, appendix_pages[0])
                add_page(page, page_index, update_footer=True)

                for extra_page in appendix_pages[1:]:
                    add_page(extra_page, generated_footer=True)

                continue

        if appendix_two_page_index is not None and page_index == appendix_two_page_index:
            mark_section("appendixTwo")
            page = update_template_header(
                    page,
                    data,
                    page_index,
                    see_below_attachment_page_index,
                    appendix_six_page_index,
                    appendix_seven_page_index
                )
            add_page(page, page_index, update_footer=True)
            log_stage("Generating Appendix 2 attachment pages")
            attachment_pages = make_appendix_two_attachment_pages(page_width, page_height, data)

            for attachment_page in attachment_pages:
                add_page(attachment_page)


            continue

        if (
            appendix_two_page_index is not None
            and appendix_two_page_index < page_index <= appendix_two_page_index + 44
        ):
            continue
        if appendix_three_page_index is not None and page_index == appendix_three_page_index:
            mark_section("appendixThree")
            page = update_template_header(
                    page,
                    data,
                    page_index,
                    see_below_attachment_page_index,
                    appendix_six_page_index,
                    appendix_seven_page_index
                )
            add_page(page, page_index, update_footer=True)
            log_stage("Generating Appendix 3 sampling photo pages")
            appendix_three_pages = make_appendix_three_sampling_photo_pages(page_width, page_height, data)

            for appendix_three_page in appendix_three_pages:
                add_page(appendix_three_page, generated_footer=True)

            continue

        if (
            appendix_three_page_index is not None
            and appendix_three_page_index < page_index <= appendix_three_page_index + 74
        ):
            continue

        if appendix_four_page_index is not None and page_index == appendix_four_page_index:
            mark_section("appendixFour")

            page = update_template_header(
                    page,
                    data,
                    page_index,
                    see_below_attachment_page_index,
                    appendix_six_page_index,
                    appendix_seven_page_index
                )
            add_page(page, page_index, update_footer=True)
            log_stage("Generating Appendix 4 layout pages")
            appendix_four_pages = make_appendix_four_location_plan_pages(
                page_number=154,
                job_id=job_id,
                data=data
            )

            for index, appendix_four_page in enumerate(appendix_four_pages):
                if index == 0:
                    add_page(appendix_four_page)
                else:
                    add_page(appendix_four_page, generated_footer=True)

            continue
        if (
            appendix_four_page_index is not None
            and appendix_four_page_index < page_index <= appendix_four_page_index + 4
        ):
            continue
        if appendix_five_page_index is not None and page_index == appendix_five_page_index:
            mark_section("appendixFive")
            log_stage("Generating Appendix 5 vessel document pages")
            appendix_five_pages = make_appendix_five_vessel_document_pages(page_width, page_height, data)

            if appendix_five_pages:
                page = update_template_header(
                        page,
                        data,
                        page_index,
                        see_below_attachment_page_index,
                        appendix_six_page_index,
                        appendix_seven_page_index
                    )
                page = merge_page_with_overlay_on_top(page, appendix_five_pages[0])
                add_page(page, page_index, update_footer=True)

                bullet_page_count = len(_make_bullet_pages(page_width, page_height, data))

                for index, appendix_five_page in enumerate(appendix_five_pages[1:], start=1):

                    # Footer only for bullet pages
                    if index < bullet_page_count:
                        add_page(appendix_five_page, generated_footer=True)

                    # No footer for uploaded file pages
                    else:
                        add_page(appendix_five_page)
            else:
                page = update_template_header(
                        page,
                        data,
                        page_index,
                        see_below_attachment_page_index,
                        appendix_six_page_index,
                        appendix_seven_page_index
                    )
                add_page(page, page_index, update_footer=True)

            continue

        if (
            appendix_five_page_index is not None
            and appendix_five_page_index < page_index <= appendix_five_page_index + 6
        ):
            continue
        if appendix_six_page_index is not None and page_index == appendix_six_page_index:
            mark_section("appendixSix")

        if appendix_seven_page_index is not None and page_index == appendix_seven_page_index:
            mark_section("appendixSeven")
            log_stage("Generating Appendix 7 certificate pages")
            appendix_seven_pages = make_appendix_seven_certificate_pages(page_width, page_height, data)
            page = update_template_header(
                    page,
                    data,
                    page_index,
                    see_below_attachment_page_index,
                    appendix_six_page_index,
                    appendix_seven_page_index
                )
            add_page(page, page_index, update_footer=True)

            if appendix_seven_pages:
                for appendix_seven_page in appendix_seven_pages:
                    add_page(appendix_seven_page)

                break

            continue
        overlay_reader = create_overlay(
            page_width,
            page_height,
            page_index,
            data,
            revision_page_index=revision_page_index,
            introduction_page_index=introduction_page_index,
            vessel_particulars_page_index=vessel_particulars_page_index
        )

        is_overlay_target = (
            page_index == 0
            or page_index == 1
            or (revision_page_index is not None and page_index == revision_page_index)
            or (introduction_page_index is not None and page_index == introduction_page_index)
            or (introduction_page_index is not None and page_index == introduction_page_index + 1)
            or (vessel_particulars_page_index is not None and page_index == vessel_particulars_page_index)
        )

        if overlay_reader.pages and is_overlay_target:
            page = merge_page_with_overlay_on_top(page, overlay_reader.pages[0])
        elif overlay_reader.pages:
            page.merge_page(overlay_reader.pages[0])

        if introduction_page_index is not None and page_index == introduction_page_index:
            mark_section("introduction")

        if scope_of_work_page_index is not None and page_index == scope_of_work_page_index:
            mark_section("scopeOfWork")

        if informative_sources_page_index is not None and page_index == informative_sources_page_index:
            mark_section("informativeSources")

        if vessel_particulars_page_index is not None and page_index == vessel_particulars_page_index:
            mark_section("vesselParticulars")

        if work_procedures_page_index is not None and page_index == work_procedures_page_index:
            mark_section("workProcedures")

        page = update_template_header(
                page,
                data,
                page_index,
                see_below_attachment_page_index,
                appendix_six_page_index,
                appendix_seven_page_index
            )
        add_page(page, page_index, update_footer=True)

        if vessel_particulars_page_index is not None and page_index == vessel_particulars_page_index:
            rows = build_vessel_particular_rows(data)
            vessel_next_index = draw_vessel_particular_table_page_count_index(len(rows))

            while vessel_next_index < len(rows):
                new_page, vessel_next_index = make_blank_page_with_vessel_rows(
                    page_width,
                    page_height,
                    data,
                    vessel_next_index
                )
                add_page(new_page, generated_footer=True)

        if revision_page_index is not None and page_index == revision_page_index:
            rows = build_revision_display_rows(data)
            revision_next_index = get_first_revision_page_next_index(data)

            while revision_next_index < len(rows):
                new_page, revision_next_index = make_blank_page_with_revision_rows(
                    page_width,
                    page_height,
                    data,
                    revision_next_index
                )
                add_page(new_page, generated_footer=True)

    default_page = template_reader.pages[0]
    default_page_width = float(default_page.mediabox.width)
    default_page_height = float(default_page.mediabox.height)

    custom_appendices = data.get("customAppendices") or []
    custom_groups = {}

    for row in custom_appendices:
        appendix_no = row.get("appendixNo")

        if appendix_no is None:
            continue

        appendix_no = int(appendix_no)

        if appendix_no not in custom_groups:
            custom_groups[appendix_no] = (
                row.get("appendixTitle") or "ADDITIONAL DOCUMENTS"
            )
    log_stage("Generating custom appendix pages")
    custom_pages = make_custom_appendix_pages(
    default_page_width,
    default_page_height,
    data,
    start_page_number=len(writer.pages)
    )

    custom_appendix_page_map = {}
    custom_start_page = len(writer.pages)
    search_from_index = 0

    # Find each appendix title page from the actual generated output.
    # Therefore multi-page uploaded PDFs are automatically included.
    for appendix_no in sorted(custom_groups.keys()):
        marker = f"APPENDIX {appendix_no}"

        for page_index in range(search_from_index, len(custom_pages)):
            try:
                page_text = (
                    custom_pages[page_index].extract_text() or ""
                ).upper()
            except Exception:
                page_text = ""

            if marker in page_text:
                custom_appendix_page_map[appendix_no] = (
                    custom_start_page + page_index
                )
                search_from_index = page_index + 1
                break

        # Fallback only when title text cannot be extracted.
        if appendix_no not in custom_appendix_page_map:
            custom_appendix_page_map[appendix_no] = (
                custom_start_page + search_from_index
            )

    custom_title_page_indexes = set(custom_appendix_page_map.values())

    for custom_page_index, custom_page in enumerate(custom_pages):
        actual_page_number = custom_start_page + custom_page_index

        if actual_page_number in custom_title_page_indexes:
            add_page(custom_page, generated_footer=True)
        else:
            add_page(custom_page)

    contents_rows = [
    {"number": "1", "title": "INTRODUCTION", "pageNo": section_pages.get("introduction", "-")},
    {"number": "2", "title": "SCOPE OF WORK", "pageNo": section_pages.get("scopeOfWork", "-")},
    {"number": "2.1", "title": "INFORMATIVE SOURCES", "pageNo": section_pages.get("informativeSources", "-")},
    {"number": "3", "title": "VESSEL PARTICULARS", "pageNo": section_pages.get("vesselParticulars", "-")},
    {"number": "4", "title": "WORK PROCEDURES", "pageNo": section_pages.get("workProcedures", "-")},
    {"number": "5", "title": "COLLECTION OF NECESSARY INFORMATION", "pageNo": section_pages.get("collectionInformation", "-")},
    {"number": "6", "title": "ASSESSING COLLECTED INFORMATION", "pageNo": section_pages.get("assessingCollectedInformation", "-")},
    {"number": "6.1", "title": "DEVELOPMENT OF VISUAL/SAMPLING CHECK PLAN (VSCP)", "pageNo": section_pages.get("visualSamplingPlan", "-")},
    {"number": "7", "title": "ONBOARD VISUAL/SAMPLING CHECK", "pageNo": section_pages.get("onboardVisualSampling", "-")},
    {"number": "7.1", "title": "METHOD OF SURVEY", "pageNo": section_pages.get("onboardVisualSampling", "-")},
    {"number": "7.2", "title": "ONBOARD SURVEY", "pageNo": section_pages.get("onboardVisualSampling", "-")},
    {"number": "7.3", "title": "SAMPLING", "pageNo": section_pages.get("onboardVisualSampling", "-")},
    {"number": "7.4", "title": "RISK ASSESSMENT", "pageNo": section_pages.get("riskAssessment", "-")},
    {"number": "", "title": "APPENDIX 1 - RESULTS FROM THE SAMPLING CHECK / VISUAL FOR PART 1 OF THE IHM", "pageNo": section_pages.get("appendixOne", "-")},
    {"number": "", "title": "APPENDIX 2 - TEST RESULTS FOR HAZARDOUS MATERIALS", "pageNo": section_pages.get("appendixTwo", "-")},
    {"number": "", "title": "APPENDIX 3 - PHOTOGRAPHS OF SAMPLING", "pageNo": section_pages.get("appendixThree", "-")},
    {"number": "", "title": "APPENDIX 4 - VISUAL / SAMPLING LOCATION MARKING PLAN", "pageNo": section_pages.get("appendixFour", "-")},
    {"number": "", "title": "APPENDIX 5 - VESSEL DOCUMENTS", "pageNo": section_pages.get("appendixFive", "-")},
    {"number": "", "title": "APPENDIX 6 - MAINTENANCE PROCEDURE FOR IHM PART I", "pageNo": section_pages.get("appendixSix", "-")},
    {"number": "", "title": "APPENDIX 7 - QUALIFICATION AND CAPABILITY CERTIFICATE", "pageNo": section_pages.get("appendixSeven", "-")},
    ]

    for appendix_no in sorted(custom_groups.keys()):

        contents_rows.append({
            "number": "",
            "title": f"APPENDIX {appendix_no} - {custom_groups[appendix_no]}",
            "pageNo": custom_appendix_page_map.get(appendix_no, "-")
        })

    if contents_insert_index is not None:
        log_stage("Generating contents/index pages")
        contents_pages = make_contents_pages(
            contents_page_width or default_page_width,
            contents_page_height or default_page_height,
            data,
            contents_rows
        )

        contents_extra_pages = max(len(contents_pages) - 1, 0)

        if contents_extra_pages:
            for row in contents_rows:
                page_no = row.get("pageNo")

                if isinstance(page_no, int) and page_no > contents_insert_index:
                    row["pageNo"] = page_no + contents_extra_pages

            contents_pages = make_contents_pages(
                contents_page_width or default_page_width,
                contents_page_height or default_page_height,
                data,
                contents_rows
            )
        

        writer, footer_flags = replace_contents_placeholder(
            writer,
            footer_flags,
            contents_insert_index,
            contents_pages
        )
    log_stage("Renumbering visible footers")
    writer, visible_total_pages = renumber_visible_footers(writer, data, footer_flags)
    writer = add_total_page_count_to_cover(writer, visible_total_pages)
    output_pdf.parent.mkdir(parents=True, exist_ok=True)

    for page in writer.pages:
        try:
            page.compress_content_streams(level=6)
        except Exception:
            pass

    log_stage("Writing final PDF file")

    with open(output_pdf, "wb") as f:
        writer.write(f)

    log_stage("COMPLETED IHM PDF generation successfully")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        error_text = traceback.format_exc()
        print(error_text, file=sys.stderr, flush=True)

        try:
            if len(sys.argv) >= 3:
                output_pdf = Path(sys.argv[2])
                log_path = output_pdf.parent / "generator.log"
                output_pdf.parent.mkdir(parents=True, exist_ok=True)

                with log_path.open("a", encoding="utf-8") as log_file:
                    log_file.write(f"{datetime.now():%Y-%m-%d %H:%M:%S} | ERROR\n")
                    log_file.write(error_text + "\n")
        except Exception:
            pass

        raise