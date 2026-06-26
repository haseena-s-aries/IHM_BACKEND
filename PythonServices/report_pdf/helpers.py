from pathlib import Path
from urllib.parse import unquote, urlparse
from datetime import datetime
from pypdf._page import PageObject
from reportlab.lib import colors
from reportlab.lib.utils import ImageReader
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from io import BytesIO
from pypdf import PdfReader
from reportlab.pdfgen import canvas

FONT_CALIBRI = "Calibri"
FONT_CALIBRI_BOLD = "Calibri-Bold"
FONT_ARIAL = "Arial"
FONT_ARIAL_BOLD = "Arial-Bold"
FONT_TIMES = "Times-Roman"


def register_fonts():
    python_services_dir = Path(__file__).resolve().parent.parent
    font_dir = python_services_dir / "fonts"

    arial = font_dir / "arial.ttf"
    arial_bold = font_dir / "arialbd.ttf"

    if not arial.exists():
        raise FileNotFoundError(f"Arial font not found: {arial}")

    if not arial_bold.exists():
        raise FileNotFoundError(f"Arial bold font not found: {arial_bold}")

    pdfmetrics.registerFont(TTFont(FONT_ARIAL, str(arial)))
    pdfmetrics.registerFont(TTFont(FONT_ARIAL_BOLD, str(arial_bold)))


def txt(value):
    if value is None:
        return ""
    return str(value).strip()

def draw_link_text(c, x, y, value, font="Times-Roman", size=8):
    value = txt(value)
    if not value:
        return

    c.setFillColor(colors.blue)
    c.setFont(font, size)
    c.drawString(x, y, value)

    text_width = pdfmetrics.stringWidth(value, font, size)
    c.setStrokeColor(colors.blue)
    c.setLineWidth(0.4)
    c.line(x, y - 1, x + text_width, y - 1)

    c.setFillColor(colors.black)
    c.setStrokeColor(colors.black)

def normalize_keys(value):
    if isinstance(value, list):
        return [normalize_keys(x) for x in value]

    if isinstance(value, dict):
        result = {}
        for key, item in value.items():
            if not key:
                continue
            camel = key[:1].lower() + key[1:]
            result[camel] = normalize_keys(item)
        return result

    return value

def format_date(value):
    value = txt(value)
    if not value:
        return ""

    try:
        date_part = value.split("T")[0]
        year, month, day = date_part.split("-")
        return f"{day}.{month}.{year}"
    except Exception:
        return value

def format_build_date(value):
    value = txt(value)

    if not value:
        return ""

    try:
        date_part = value.split("T")[0]
        dt = datetime.strptime(date_part, "%Y-%m-%d")

        return dt.strftime("%d %b %Y").upper()

    except Exception:
        return value

def format_number(value, suffix=""):
    value = txt(value)
    if not value:
        return ""

    try:
        number = float(value)
        value = f"{number:g}"
    except Exception:
        pass

    return f"{value} {suffix.lower()}".strip()

def draw_text(c, x, y, value, font="Times-Roman", size=9, color=colors.black):
    value = txt(value)
    if not value:
        return

    c.setFillColor(color)
    c.setFont(font, size)
    c.drawString(x, y, value)
    c.setFillColor(colors.black)


def draw_centered_text(c, x1, y1, x2, y2, value, font="Times-Roman", size=9):
    value = txt(value)
    if not value:
        return

    width = x2 - x1
    height = y2 - y1

    c.setFont(font, size)
    text_width = pdfmetrics.stringWidth(value, font, size)

    x = x1 + (width - text_width) / 2
    y = y1 + (height - size) / 2 + 2

    c.drawString(x, y, value)

def draw_image_in_box(c, image_path, x1, y1, x2, y2):
    file_path = resolve_file_path(image_path)
    if not file_path:
        return False

    img = ImageReader(file_path)
    img_w, img_h = img.getSize()

    box_w = x2 - x1
    box_h = y2 - y1

    scale = min(box_w / img_w, box_h / img_h)
    final_w = img_w * scale
    final_h = img_h * scale

    final_x = x1 + (box_w - final_w) / 2
    final_y = y1 + (box_h - final_h) / 2

    c.drawImage(
        img,
        final_x,
        final_y,
        final_w,
        final_h,
        preserveAspectRatio=True,
        mask="auto"
    )

    return True

def split_text_to_lines(text, font, size, max_width, max_lines=3):
    value = txt(text)
    if not value:
        return []

    words = value.split()
    lines = []
    current = ""

    for word in words:
        test = f"{current} {word}".strip()
        if pdfmetrics.stringWidth(test, font, size) <= max_width:
            current = test
        else:
            if current:
                lines.append(current)
            current = word

        if len(lines) >= max_lines:
            break

    if current and len(lines) < max_lines:
        lines.append(current)

    return lines

def wrap_text_to_lines(text, font, size, max_width):
    value = txt(text) or "-"
    lines = []

    for paragraph in value.splitlines():
        words = paragraph.split()
        current = ""

        for word in words:
            test = f"{current} {word}".strip()
            if pdfmetrics.stringWidth(test, font, size) <= max_width:
                current = test
            else:
                if current:
                    lines.append(current)
                current = word

        if current:
            lines.append(current)

    return lines or ["-"]


def calculate_row_height(values, col_widths, min_height=25, font=FONT_TIMES, size=9, padding=8):
    max_lines = 1

    for value, col_width in zip(values, col_widths):
        lines = wrap_text_to_lines(value, font, size, col_width - 6)
        max_lines = max(max_lines, len(lines))

    return max(min_height, (max_lines * (size + 2)) + padding)

def fit_text_to_width(c, text, font, size, max_width):
    value = txt(text)
    while value and pdfmetrics.stringWidth(value, font, size) > max_width:
        value = value[:-1]
    return value

def draw_common_header_footer(c, vessel_name="", company_name="", page_no_text="Page | "):
    page_width = c._pagesize[0]
    page_height = c._pagesize[1]

    top_line_y = page_height - 60
    top_text_y = page_height - 48
    footer_line_y = 53
    footer_text_y = 38

    left_x = 70
    right_x = page_width - 70

    c.setStrokeColor(colors.HexColor("#A0A0A0"))
    c.setLineWidth(1.5)
    c.line(left_x, top_line_y, right_x, top_line_y)

    c.setFillColor(colors.black)
    c.setFont("Times-Italic", 12)
    c.drawString(left_x + 6, top_text_y, "INVENTORY OF HAZARDOUS MATERIALS PART I")

    vessel = txt(vessel_name).upper()
    vessel = fit_text_to_width(c, vessel, "Times-Italic", 12, 260)
    vessel_width = pdfmetrics.stringWidth(vessel, "Times-Italic", 12)
    c.drawString(right_x - vessel_width, top_text_y, vessel)

    c.setStrokeColor(colors.black)
    c.setLineWidth(0.5)
    c.line(left_x, footer_line_y, right_x, footer_line_y)

    c.setFillColor(colors.black)
    c.setFont("Times-Italic", 12)
    c.drawString(left_x, footer_text_y, txt(company_name).upper())

    page_label = "P a g e  |"
    page_no = txt(page_no_text).replace("Page |", "").strip()

    c.setFont("Times-Italic", 12)
    c.setFillColor(colors.HexColor("#8A8A8A"))

    page_label_width = pdfmetrics.stringWidth(page_label, "Times-Italic", 12)

    page_no_reserved_width = 22
    label_x = right_x - page_label_width - page_no_reserved_width

    c.drawString(label_x, footer_text_y, page_label)

    if page_no:
        c.setFillColor(colors.black)
        c.setFont("Times-BoldItalic", 12)
        page_no_width = pdfmetrics.stringWidth(page_no, "Times-BoldItalic", 12)
        c.drawString(right_x - page_no_width, footer_text_y, page_no)

    c.setStrokeColor(colors.black)
    c.setFillColor(colors.black)


def merge_page_with_overlay_on_top(page, overlay_page):
    try:
        page.transfer_rotation_to_content()
    except Exception:
        pass

    page.merge_page(
        overlay_page,
        over=True,
        expand=False
    )

    return page

def extract_page_texts(reader):
    texts = []
    for page in reader.pages:
        try:
            texts.append(page.extract_text() or "")
        except Exception:
            texts.append("")
    return texts


def find_page_by_text_after(page_texts, keyword, start_page=2):
    keyword = txt(keyword).upper()
    for index in range(start_page, len(page_texts)):
        if keyword in txt(page_texts[index]).upper():
            return index
    return None

def replace_template_header_vessel_name(page, vessel_name):
    vessel = txt(vessel_name).upper()
    if not vessel:
        return page

    try:
        page.transfer_rotation_to_content()
    except Exception:
        pass

    page_width = float(page.mediabox.width)
    page_height = float(page.mediabox.height)

    erase_x = 350
    right_x = page_width - 70

    erase_bottom_y = page_height - 55
    erase_top_y = page_height - 30

    text_y = page_height - 48

    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    c.setFillColor(colors.white)
    c.rect(
        erase_x,
        erase_bottom_y,
        page_width - erase_x,
        erase_top_y - erase_bottom_y,
        stroke=0,
        fill=1
    )

    c.setFillColor(colors.black)
    c.setFont("Times-Italic", 12)

    vessel = fit_text_to_width(
        c,
        vessel,
        "Times-Italic",
        12,
        right_x - erase_x
    )

    vessel_width = pdfmetrics.stringWidth(vessel, "Times-Italic", 12)
    c.drawString(right_x - vessel_width, text_y, vessel)

    c.save()
    packet.seek(0)

    overlay_page = PdfReader(packet).pages[0]
    return merge_page_with_overlay_on_top(page, overlay_page)

def replace_template_footer(page, company_name, page_number):
    company = txt(company_name).upper()
    page_no = txt(page_number)

    try:
        page.transfer_rotation_to_content()
    except Exception:
        pass

    page_width = float(page.mediabox.width)
    page_height = float(page.mediabox.height)

    left_x = 70
    right_x = page_width - 70

    erase_top_y = 47
    #footer_line_y = 53
    footer_text_y = 37

    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    # erase everything from y=45 down to page bottom
    c.setFillColor(colors.white)
    c.rect(
        0,
        0,
        page_width,
        erase_top_y,
        stroke=0,
        fill=1
    )

    # draw footer line
    #c.setStrokeColor(colors.black)
    #c.setLineWidth(0.5)
    #c.line(left_x, footer_line_y, right_x, footer_line_y)

    # company left
    c.setFillColor(colors.black)
    c.setFont("Times-Italic", 12)
    c.drawString(left_x, footer_text_y, company)

    # right page label
    page_label = "P a g e  |"
    page_no_reserved_width = 22

    c.setFont("Times-Italic", 12)
    c.setFillColor(colors.HexColor("#8A8A8A"))

    page_label_width = pdfmetrics.stringWidth(page_label, "Times-Italic", 12)
    label_x = right_x - page_label_width - page_no_reserved_width
    c.drawString(label_x, footer_text_y, page_label)

    # page number only bold italic
    if page_no:
        c.setFillColor(colors.black)
        c.setFont("Times-BoldItalic", 12)
        page_no_width = pdfmetrics.stringWidth(page_no, "Times-BoldItalic", 12)
        c.drawString(right_x - page_no_width, footer_text_y, page_no)

    c.setStrokeColor(colors.black)
    c.setFillColor(colors.black)

    c.save()
    packet.seek(0)

    overlay_page = PdfReader(packet).pages[0]
    return merge_page_with_overlay_on_top(page, overlay_page)

def resolve_file_path(path_value):
    value = txt(path_value)
    if not value:
        return None

    parsed = urlparse(value)
    if parsed.scheme and parsed.netloc:
        value = parsed.path

    value = unquote(value).replace("\\", "/").split("?", 1)[0].split("#", 1)[0].strip()
    if not value:
        return None

    path = Path(value)
    if path.is_absolute() and path.exists():
        return str(path)

    helpers_dir = Path(__file__).resolve().parent
    python_services_dir = helpers_dir.parent
    project_dir = python_services_dir.parent
    wwwroot_dir = project_dir / "wwwroot"

    clean_value = value.lstrip("/")

    candidates = [
        project_dir / clean_value,
        wwwroot_dir / clean_value,
        python_services_dir / clean_value,
    ]

    file_name = Path(clean_value).name
    if file_name:
        candidates.append(wwwroot_dir / "company-logos" / file_name)

    for candidate in candidates:
        if candidate.exists():
            return str(candidate)

    return None
    