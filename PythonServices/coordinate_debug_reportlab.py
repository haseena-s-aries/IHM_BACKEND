from io import BytesIO
from pathlib import Path

from pypdf import PdfReader, PdfWriter
from pypdf._page import PageObject
from reportlab.pdfgen import canvas


def create_grid_overlay(page_width, page_height, step=20):
    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    c.setLineWidth(0.7)
    c.setStrokeColorRGB(1, 0, 0)
    c.setFillColorRGB(0, 0, 1)
    c.setFont("Helvetica-Bold", 6)

    x = 0
    while x <= page_width:
        c.line(x, 0, x, page_height)
        c.drawString(x + 2, page_height - 12, str(int(x)))
        c.drawString(x + 2, 5, str(int(x)))
        x += step

    y = 0
    while y <= page_height:
        c.line(0, y, page_width, y)
        c.drawString(3, y + 3, str(int(y)))
        c.drawString(page_width - 30, y + 3, str(int(y)))
        y += step

    c.setFont("Helvetica-Bold", 18)
    c.setFillColorRGB(1, 0, 0)
    c.drawString(40, page_height - 40, "GRID DEBUG")

    c.save()
    packet.seek(0)
    return PdfReader(packet)


def generate_coordinate_pdf(template_pdf_path, output_pdf_path, page_number=None, step=20):
    template_pdf_path = Path(template_pdf_path)
    output_pdf_path = Path(output_pdf_path)

    reader = PdfReader(str(template_pdf_path))
    writer = PdfWriter()

    for page_index, original_page in enumerate(reader.pages):
        page = original_page

        try:
            page.transfer_rotation_to_content()
        except Exception:
            pass

        page_width = float(page.mediabox.width)
        page_height = float(page.mediabox.height)

        if page_number is None or page_index == page_number:
            blank = PageObject.create_blank_page(
                width=page_width,
                height=page_height
            )

            blank.merge_page(page)

            overlay = create_grid_overlay(page_width, page_height, step)
            blank.merge_page(overlay.pages[0])

            writer.add_page(blank)
        else:
            writer.add_page(page)

    output_pdf_path.parent.mkdir(parents=True, exist_ok=True)

    with open(output_pdf_path, "wb") as f:
        writer.write(f)

    return str(output_pdf_path)