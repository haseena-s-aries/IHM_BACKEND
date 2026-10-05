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

    checkpoint_photos = [
        photo for photo in photos
        if str(photo.get("ihmCheckpointId")) == str(checkpoint_id)
    ]

    selected_photos = [
        photo for photo in checkpoint_photos
        if bool(photo.get("showInAppendixThree") or photo.get("ShowInAppendixThree"))
    ]

    if selected_photos:
        matching_photos = selected_photos
        matching_photos.sort(
            key=lambda photo: (
                int(photo.get("appendixThreeSortOrder") or photo.get("AppendixThreeSortOrder") or 0),
                txt(photo.get("uploadedAt") or photo.get("UploadedAt")),
            )
        )
    else:
        matching_photos = checkpoint_photos
        matching_photos.sort(
            key=lambda photo: txt(photo.get("uploadedAt") or photo.get("UploadedAt")),
            reverse=True
        )

    for photo in matching_photos:
        edited_path = photo.get("appendixThreeEditedFilePath") or photo.get("AppendixThreeEditedFilePath")
        if edited_path:
            photo["filePath"] = edited_path

    return matching_photos[:2]


def _draw_wrapped_centered_text(c, text, x1, y1, x2, y2, font=FONT_TIMES, size=12):
    padding = 10
    lines = _appendix3_wrap_lines(text, font, size, max(1, x2 - x1 - (padding * 2)))

    line_height = max(size + 3, 15)
    total_height = len(lines) * line_height
    start_y = y1 + ((y2 - y1) + total_height) / 2 - size

    c.setFont(font, size)
    c.setFillColor(colors.black)

    for index, line in enumerate(lines):
        line_width = pdfmetrics.stringWidth(line, font, size)
        line_x = x1 + ((x2 - x1) - line_width) / 2
        c.drawString(line_x, start_y - (index * line_height), line)


def _appendix3_wrap_lines(value, font, size, max_width):
    lines = []
    for paragraph in txt(value).splitlines() or [""]:
        words = paragraph.split() or [""]
        current = ""
        for word in words:
            candidate = f"{current} {word}".strip()
            if not current or pdfmetrics.stringWidth(candidate, font, size) <= max_width:
                current = candidate
                continue
            lines.append(current)
            current = ""

        if current:
            # Break any remaining unbreakable word at character boundaries.
            if pdfmetrics.stringWidth(current, font, size) <= max_width:
                lines.append(current)
            else:
                chunk = ""
                for character in current:
                    if chunk and pdfmetrics.stringWidth(chunk + character, font, size) > max_width:
                        lines.append(chunk)
                        chunk = character
                    else:
                        chunk += character
                if chunk:
                    lines.append(chunk)
    return lines or ["-"]


def _wrapped_row_height(value, width, size=12):
    """Return a row height that fully contains every wrapped line."""
    padding = 10
    line_height = max(size + 3, 15)
    lines = _appendix3_wrap_lines(value, FONT_TIMES, size, max(1, width - (padding * 2)))
    return max(35, (len(lines) * line_height) + (padding * 2))


APPENDIX3_LABEL_FONT_SIZE = 10
APPENDIX3_VALUE_FONT_SIZE = 12


def _draw_row(c, label, value, y_top, y_bottom):
    # Labels use a slightly smaller font because the left column is narrow;
    # both label and value remain vertically and horizontally centered.
    _draw_wrapped_centered_text(
        c,
        label,
        70,
        y_bottom,
        160,
        y_top,
        size=APPENDIX3_LABEL_FONT_SIZE,
    )
    _draw_wrapped_centered_text(
        c,
        value,
        160,
        y_bottom,
        520,
        y_top,
        size=APPENDIX3_VALUE_FONT_SIZE,
    )

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
    item_value = _item_description(checkpoint)
    hazard_value = checkpoint.get("hazardousMaterial")
    method_value = _method_of_inspection(checkpoint)
    result_label = _result_label(checkpoint)
    result_value = _result_value(checkpoint)
    quantity_value = _quantity_text(checkpoint)

    value_width = x_right - x_first
    label_width = x_first - x_left
    y_item = y_image_bottom - max(
        _wrapped_row_height(item_value, value_width, APPENDIX3_VALUE_FONT_SIZE),
        _wrapped_row_height("ITEM\nDESCRIPTION", label_width, APPENDIX3_LABEL_FONT_SIZE),
    )
    y_hazard = y_item - max(
        _wrapped_row_height(hazard_value, value_width, APPENDIX3_VALUE_FONT_SIZE),
        _wrapped_row_height("HAZARDOUS\nMATERIAL\nTESTED FOR", label_width, APPENDIX3_LABEL_FONT_SIZE),
    )
    y_method = y_hazard - max(
        _wrapped_row_height(method_value, value_width, APPENDIX3_VALUE_FONT_SIZE),
        _wrapped_row_height("METHODS OF\nINSPECTION", label_width, APPENDIX3_LABEL_FONT_SIZE),
    )
    y_result = y_method - max(
        _wrapped_row_height(result_label, label_width, APPENDIX3_LABEL_FONT_SIZE),
        _wrapped_row_height(result_value, value_width, APPENDIX3_VALUE_FONT_SIZE),
    )
    y_quantity = y_result - max(
        _wrapped_row_height("QUANTITY", label_width, APPENDIX3_LABEL_FONT_SIZE),
        _wrapped_row_height(quantity_value, value_width, APPENDIX3_VALUE_FONT_SIZE),
    )
    # Appendix 3 ends after Quantity. Site remarks are shown in the Visual /
    # Sampling Check Plan Remarks column instead of as a separate row.
    y_bottom = y_quantity

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
        item_value,
    y_image_bottom,
    y_item
)

    _draw_row(
        c,
        "HAZARDOUS\nMATERIAL\nTESTED FOR",
        hazard_value,
        y_item,
        y_hazard
    )

    _draw_row(
        c,
        "METHODS OF\nINSPECTION",
        method_value,
        y_hazard,
        y_method
    )

    _draw_row(
        c,
        result_label,
        result_value,
        y_method,
        y_result
    )

    _draw_row(
        c,
        "QUANTITY",
        quantity_value,
        y_result,
        y_quantity
    )



def make_appendix_three_sampling_photo_pages(page_width, page_height, data):
    checkpoints = list(data.get("ihmCheckpoints") or [])
    # Keep Appendix 3 pages in the latest Excel row order. Sample_No can be
    # regenerated (S/V prefixes), so it is not a reliable ordering key.
    checkpoints.sort(key=lambda row: (
        int(row.get("sourceRowNumber") or row.get("SourceRowNumber") or 2**31 - 1),
        int(row.get("id") or row.get("Id") or 2**31 - 1),
    ))
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
