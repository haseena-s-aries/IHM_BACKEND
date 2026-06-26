from io import BytesIO
from pathlib import Path

from pypdf import PdfReader
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen import canvas

from report_pdf.helpers import resolve_file_path, txt


IMAGE_EXTENSIONS = {".png", ".jpg", ".jpeg"}


def get_appendix_two_attachments(data):
    return data.get("appendixTwoAttachments") or []


def image_to_pdf_page(file_path, page_width, page_height):
    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    img = ImageReader(file_path)
    img_w, img_h = img.getSize()

    margin = 35
    box_w = page_width - (margin * 2)
    box_h = page_height - (margin * 2)

    scale = min(box_w / img_w, box_h / img_h)
    final_w = img_w * scale
    final_h = img_h * scale

    x = (page_width - final_w) / 2
    y = (page_height - final_h) / 2

    c.drawImage(
        img,
        x,
        y,
        final_w,
        final_h,
        preserveAspectRatio=True,
        mask="auto",
    )

    c.save()
    packet.seek(0)

    reader = PdfReader(packet)
    return reader.pages[0]


def make_appendix_two_attachment_pages(page_width, page_height, data):
    pages = []

    for item in get_appendix_two_attachments(data):
        file_path = resolve_file_path(item.get("filePath"))

        if not file_path:
            continue

        extension = Path(file_path).suffix.lower()

        if extension == ".pdf":
            reader = PdfReader(file_path)
            pages.extend(reader.pages)
            continue

        if extension in IMAGE_EXTENSIONS:
            pages.append(image_to_pdf_page(file_path, page_width, page_height))

    return pages