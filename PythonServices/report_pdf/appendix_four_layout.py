from io import BytesIO
from pathlib import Path
from PIL import Image
from pypdf import PdfReader
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen import canvas
from pypdf._page import PageObject
from report_pdf.helpers import draw_common_header_footer

def make_area_layout_page(image_path, data=None):
    data = data or {}
    job = data.get("job") or {}
    vessel = data.get("vessel") or {}
    report = data.get("reportInfo") or {}

    vessel_name = vessel.get("vesselName") or job.get("vesselName") or ""
    company_name = report.get("companyName") or ""

    with Image.open(image_path) as img:
        img_width, img_height = img.size

    is_wide = img_width > img_height

    
    page_width = 840
    page_height = 580
    

    page = PageObject.create_blank_page(
        width=page_width,
        height=page_height
    )

    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    draw_common_header_footer(
        c,
        vessel_name=vessel_name,
        company_name=company_name,
        page_no_text=""
    )

    image_x = 45
    image_y = 65
    image_right = page_width - 45
    image_top = page_height - 80

    image_width = image_right - image_x
    image_height = image_top - image_y

    c.drawImage(
        ImageReader(str(image_path)),
        image_x,
        image_y,
        width=image_width,
        height=image_height,
        preserveAspectRatio=True,
        anchor="c",
        mask="auto",
    )

    c.save()
    packet.seek(0)

    overlay_page = PdfReader(packet).pages[0]
    page.merge_page(overlay_page)

    return page

def make_appendix_four_location_plan_pages(page_number=1, job_id=None, data=None):
    base_dir = Path(__file__).resolve().parent.parent
    project_root = Path(__file__).resolve().parents[2]
    source_pdf = base_dir / "templates" / "report.pdf"

    if job_id is None:
        return []

    map_image = (
        project_root
        / "wwwroot"
        / "reports"
        / "ihm"
        / str(job_id)
        / "appendix4-ga-marker-map.png"
    )

    if not source_pdf.exists():
        return []

    reader = PdfReader(str(source_pdf))
    page_index = max(int(page_number) - 1, 0)

    if page_index >= len(reader.pages):
        return []

    page = reader.pages[page_index]
    print("Appendix 4 media box:", page.mediabox)
    print("Appendix 4 crop box:", page.cropbox)
    print("Appendix 4 rotation:", page.rotation)
    if page.rotation:
        page.transfer_rotation_to_content()

    image_exists = map_image.exists()

    page_width = float(page.mediabox.width)
    page_height = float(page.mediabox.height)

    print("Appendix 4 page size:", page_width, page_height)
    print("Appendix 4 rotation after fix:", page.rotation)

    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    c.setFillColorRGB(1, 1, 1)

    c.rect(100, 480, 1660 - 100, 900 - 480, fill=1, stroke=0)
    c.rect(100, 35, 1940 - 100, 540 - 35, fill=1, stroke=0)

    image_x = 60
    image_y = 40
    image_right = 1660
    image_top = 900

    image_width = image_right - image_x
    image_height = image_top - image_y

    if image_exists:
        c.drawImage(
            ImageReader(str(map_image)),
            image_x,
            image_y,
            width=image_width,
            height=image_height,
            preserveAspectRatio=True,
            anchor="c",
            mask="auto",
        )

    c.save()
    packet.seek(0)

    overlay_page = PdfReader(packet).pages[0]
    page.merge_page(overlay_page)

    pages = [page]

    report_folder = (
        project_root
        / "wwwroot"
        / "reports"
        / "ihm"
        / str(job_id)
    )

    area_images = sorted(report_folder.glob("appendix4-area-*.png"))

    for area_image in area_images:
        area_page = make_area_layout_page(
            area_image,
            data=data
        )

        if area_page is not None:
            pages.append(area_page)

    return pages