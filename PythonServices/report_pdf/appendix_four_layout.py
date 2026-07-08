from io import BytesIO
import json
import hashlib
import sys
import time
from pathlib import Path
from PIL import Image, ImageOps
from pypdf import PdfReader
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen import canvas
from pypdf._page import PageObject
from report_pdf.helpers import draw_common_header_footer

def _stable_json(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), default=str)

def _file_fingerprint(path):
    path = Path(path)
    stat = path.stat()
    return {
        "path": str(path.resolve()),
        "mtimeNs": stat.st_mtime_ns,
        "size": stat.st_size,
    }

def _appendix4_cache_dir(job_id):
    path = Path.cwd() / "wwwroot" / "reports" / "ihm" / str(job_id or "0") / "layout-image-cache"
    path.mkdir(parents=True, exist_ok=True)
    return path

def _appendix4_cache_key(source_path, data, label):
    payload = {
        "label": label,
        "source": _file_fingerprint(source_path),
        "markers": data.get("markers") or [],
        "areas": data.get("areas") or [],
        "areaLayouts": data.get("areaLayouts") or [],
        "areaLayoutPlacements": data.get("areaLayoutPlacements") or [],
        "checkpoints": data.get("ihmCheckpoints") or [],
        "equipment": data.get("equipmentAndMachinery") or [],
    }
    return hashlib.sha256(_stable_json(payload).encode("utf-8")).hexdigest()

def _cache_image(source_path, data, label):
    job_id = ((data.get("job") or {}).get("id") or "0")
    cache_dir = _appendix4_cache_dir(job_id)
    cache_key = _appendix4_cache_key(source_path, data, label)
    cached = cache_dir / f"appendix4-{cache_key}.png"

    if cached.exists():
        print(f"CACHE HIT Appendix4 {cached}", file=sys.stderr)
        return cached

    print(f"CACHE MISS Appendix4 source={source_path}", file=sys.stderr)
    return None
def make_pdf_ready_image(image_path, target_width_pt, target_height_pt, label):
    image_path = Path(image_path)
    stat = image_path.stat()

    cache_dir = image_path.parent / "pdf-ready-images"
    cache_dir.mkdir(parents=True, exist_ok=True)

    payload = {
        "path": str(image_path.resolve()),
        "mtimeNs": stat.st_mtime_ns,
        "size": stat.st_size,
        "targetWidthPt": round(float(target_width_pt), 2),
        "targetHeightPt": round(float(target_height_pt), 2),
        "dpi": 150,
        "label": label,
    }

    cache_key = hashlib.sha256(
        json.dumps(payload, sort_keys=True).encode("utf-8")
    ).hexdigest()

    ready_path = cache_dir / f"{cache_key}.jpg"

    if ready_path.exists():
        print(f"CACHE HIT pdf-ready image {ready_path}", file=sys.stderr)
        return ready_path

    print(f"CACHE MISS pdf-ready image {image_path}", file=sys.stderr)

    target_w_px = max(1, int(float(target_width_pt) / 72 * 150))
    target_h_px = max(1, int(float(target_height_pt) / 72 * 150))

    with Image.open(image_path) as img:
        img = ImageOps.exif_transpose(img).convert("RGB")
        img.thumbnail((target_w_px, target_h_px), Image.Resampling.LANCZOS)
        img.save(ready_path, "JPEG", quality=92, optimize=True)

    return ready_path
def make_area_layout_page(image_path, data=None):
    data = data or {}
    job = data.get("job") or {}
    vessel = data.get("vessel") or {}
    report = data.get("reportInfo") or {}

    vessel_name = vessel.get("vesselName") or job.get("vesselName") or ""
    company_name = report.get("companyName") or ""

    started = time.perf_counter()
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

    pdf_ready_image = make_pdf_ready_image(
        image_path,
        image_width,
        image_height,
        "appendix4-area"
    )

    c.drawImage(
        ImageReader(str(pdf_ready_image)),
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

    print(
        f"Appendix 4 area page image processing elapsed_ms={int((time.perf_counter() - started) * 1000)} image={image_path}",
        file=sys.stderr
    )

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
        image_started = time.perf_counter()

        pdf_ready_map = make_pdf_ready_image(
            map_image,
            image_width,
            image_height,
            "appendix4-main-map"
        )

        c.drawImage(
            ImageReader(str(pdf_ready_map)),
            image_x,
            image_y,
            width=image_width,
            height=image_height,
            preserveAspectRatio=True,
            anchor="c",
            mask="auto",
        )
        print(
            f"Appendix 4 main map draw elapsed_ms={int((time.perf_counter() - image_started) * 1000)} image={map_image}",
            file=sys.stderr
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
        area_started = time.perf_counter()
        print(f"START Appendix 4 area image {area_image}", file=sys.stderr)

        area_page = make_area_layout_page(
            area_image,
            data=data
        )

        print(
            f"END Appendix 4 area image {area_image} elapsed_ms={int((time.perf_counter() - area_started) * 1000)}",
            file=sys.stderr
        )

        if area_page is not None:
            pages.append(area_page)

    return pages