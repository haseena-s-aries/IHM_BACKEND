from io import BytesIO

from pypdf import PdfReader
from reportlab.lib import colors
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfgen import canvas

from report_pdf.helpers import (
    FONT_TIMES,
    draw_common_header_footer,
    draw_image_in_box,
    resolve_file_path,
    txt,
    wrap_text_to_lines,
)


def _number_or_none(value):
    value = txt(value)

    if not value or value == "-":
        return None

    try:
        return float(value)
    except Exception:
        return None


def _quantity_text(checkpoint):
    unit_kg = _number_or_none(checkpoint.get("quantityUnitKg"))
    quantity_no = _number_or_none(checkpoint.get("quantityNo"))
    total_kg = _number_or_none(checkpoint.get("quantityTotalKg"))

    if unit_kg is None or quantity_no is None or total_kg is None:
        return "-"

    return f"{unit_kg:g} kg x {quantity_no:g} Nos. = {total_kg:g} kg"


def _method_of_inspection(checkpoint):
    if txt(checkpoint.get("samplingCheck")).upper() == "S":
        return "Sample Collection"

    if txt(checkpoint.get("visualCheck")).upper() == "V":
        return "Visual Inspection"

    return "-"


def _result_label(checkpoint):
    if txt(checkpoint.get("samplingCheck")).upper() == "S":
        return "LAB TEST RESULT"

    return "RESULT"


def _result_value(checkpoint):
    check_result = txt(checkpoint.get("checkResult"))
    hazardous_material = txt(checkpoint.get("hazardousMaterial"))

    if check_result.lower() == "no":
        return "Not detected"

    if check_result.lower() == "yes":
        return f"Containing {hazardous_material}" if hazardous_material else "Containing"

    return check_result or "-"


def _checkpoint_photos(data, checkpoint_id):
    photos = data.get("photos") or []

    matching_photos = [
        photo for photo in photos
        if str(photo.get("ihmCheckpointId")) == str(checkpoint_id)
    ]

    matching_photos.sort(
        key=lambda photo: txt(photo.get("uploadedAt")),
        reverse=True
    )

    return matching_photos[:2]


def _draw_wrapped_centered_text(c, text, x1, y1, x2, y2, font=FONT_TIMES, size=12):
    lines = wrap_text_to_lines(text, font, size, x2 - x1 - 8)

    line_height = size + 2
    total_height = len(lines) * line_height
    start_y = y1 + ((y2 - y1) + total_height) / 2 - size

    c.setFont(font, size)
    c.setFillColor(colors.black)

    for index, line in enumerate(lines):
        line_width = pdfmetrics.stringWidth(line, font, size)
        line_x = x1 + ((x2 - x1) - line_width) / 2
        c.drawString(line_x, start_y - (index * line_height), line)


def _draw_row(c, label, value, y_top, y_bottom):
    _draw_wrapped_centered_text(c, label, 70, y_bottom, 160, y_top, size=12)
    _draw_wrapped_centered_text(c, value, 160, y_bottom, 520, y_top, size=12)

def _item_description(checkpoint):
    values = [
        txt(checkpoint.get("location")),
        txt(checkpoint.get("equipmentName")),
        txt(checkpoint.get("component")),
    ]

    filtered_values = [
        value for value in values
        if value and value != "-"
    ]

    return " - ".join(filtered_values) if filtered_values else "-"

def _draw_sampling_photo_table(c, checkpoint, photos):
    x_left = 70
    x_first = 160
    x_second = 340
    x_right = 520

    y_top = 770
    y_image_bottom = 525
    y_item = 485
    y_hazard = 440
    y_method = 395
    y_result = 350
    y_quantity = 315
    y_quantity = 315
    y_bottom = 280

    c.setStrokeColor(colors.black)
    c.setLineWidth(0.7)

    for x in [x_left, x_first, x_right]:
        c.line(x, y_bottom, x, y_top)

    c.line(x_second, y_image_bottom, x_second, y_top)

    for y in [y_top, y_image_bottom, y_item, y_hazard, y_method, y_result, y_quantity, y_bottom]:
        c.line(x_left, y, x_right, y)

    _draw_wrapped_centered_text(
        c,
        checkpoint.get("sample_No") or checkpoint.get("sampleNo"),
        x_left,
        y_image_bottom,
        x_first,
        y_top,
        size=12
    )

    if len(photos) > 0:
        draw_image_in_box(c, photos[0].get("filePath"), x_first + 5, y_image_bottom + 5, x_second - 5, y_top - 5)

    if len(photos) > 1:
        draw_image_in_box(c, photos[1].get("filePath"), x_second + 5, y_image_bottom + 5, x_right - 5, y_top - 5)

    _draw_row(
    c,
    "ITEM\nDESCRIPTION",
    _item_description(checkpoint),
    y_image_bottom,
    y_item
)

    _draw_row(
        c,
        "HAZARDOUS\nMATERIAL\nTESTED FOR",
        checkpoint.get("hazardousMaterial"),
        y_item,
        y_hazard
    )

    _draw_row(
        c,
        "METHODS OF\nINSPECTION",
        _method_of_inspection(checkpoint),
        y_hazard,
        y_method
    )

    _draw_row(
        c,
        _result_label(checkpoint),
        _result_value(checkpoint),
        y_method,
        y_result
    )

    _draw_row(
        c,
        "QUANTITY",
        _quantity_text(checkpoint),
        y_result,
        y_quantity
    )

    _draw_row(
        c,
        "SITE\nREMARKS",
        checkpoint.get("locationRemarks") or checkpoint.get("LocationRemarks") or checkpoint.get("Location_Remark") or "-",
        y_quantity,
        y_bottom
    )


def make_appendix_three_sampling_photo_pages(page_width, page_height, data):
    checkpoints = data.get("ihmCheckpoints") or []
    vessel = data.get("vessel") or {}
    report_info = data.get("reportInfo") or {}

    pages = []

    for checkpoint in checkpoints:
        packet = BytesIO()
        c = canvas.Canvas(packet, pagesize=(page_width, page_height))

        draw_common_header_footer(
            c,
            vessel_name=vessel.get("vesselName"),
            company_name=report_info.get("companyName"),
            page_no_text="Page | "
        )

        photos = _checkpoint_photos(data, checkpoint.get("id"))
        _draw_sampling_photo_table(c, checkpoint, photos)

        c.save()
        packet.seek(0)

        pages.append(PdfReader(packet).pages[0])

    return pages
