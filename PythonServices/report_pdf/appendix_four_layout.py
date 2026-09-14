from io import BytesIO
import json
import hashlib
import sys
import time
from io import BytesIO
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

    target_w_px = max(1, int(float(target_width_pt) / 72 * 450))
    target_h_px = max(1, int(float(target_height_pt) / 72 * 450))

    ready_path = image_path.parent / f"{image_path.stem}-pdf-ready.tmp.png"

    with Image.open(image_path) as img:
        img = ImageOps.exif_transpose(img).convert("RGB")

        if img.width < target_w_px or img.height < target_h_px:
            scale = min(target_w_px / img.width, target_h_px / img.height)
            new_size = (max(1, int(img.width * scale)), max(1, int(img.height * scale)))
            img = img.resize(new_size, Image.Resampling.LANCZOS)
        else:
            img.thumbnail((target_w_px, target_h_px), Image.Resampling.LANCZOS)

        img.save(ready_path, "PNG", dpi=(450, 450))

    return ready_path
def make_area_layout_page(image_path, data=None):
    data = data or {}
    started = time.perf_counter()

    with Image.open(image_path) as img:
        img = ImageOps.exif_transpose(img)
        img_width, img_height = img.size

    max_page_width = 1600
    max_page_height = 1100
    horizontal_margin = 45
    header_footer_space = 150

    scale = min(
        (max_page_width - (horizontal_margin * 2)) / img_width,
        (max_page_height - header_footer_space) / img_height,
        1
    )
    draw_width = img_width * scale
    draw_height = img_height * scale
    page_width = max(840, min(max_page_width, draw_width + (horizontal_margin * 2)))
    page_height = max(580, min(max_page_height, draw_height + header_footer_space))

    page = PageObject.create_blank_page(
        width=page_width,
        height=page_height
    )

    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(page_width, page_height))

    report = data.get("reportInfo") or {}
    vessel = data.get("vessel") or {}
    job = data.get("job") or {}
    draw_common_header_footer(
        c,
        vessel_name=vessel.get("vesselName") or job.get("vesselName"),
        company_name=report.get("companyName"),
        page_no_text="Page | "
    )

    pdf_ready_image = make_pdf_ready_image(
        image_path,
        draw_width,
        draw_height,
        "appendix4-area"
    )

    c.drawImage(
        ImageReader(str(pdf_ready_image)),
        (page_width - draw_width) / 2,
        75 + (page_height - 150 - draw_height) / 2,
        width=draw_width,
        height=draw_height,
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
    section_started = time.perf_counter()
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
    pre_generated_images = (
        (data or {}).get("preGeneratedMarkedImages")
        or (data or {}).get("PreGeneratedMarkedImages")
        or {}
    )

    pre_generated = (
        pre_generated_images.get("appendix4")
        or pre_generated_images.get("Appendix4")
    )
    print(f"Appendix4 preGeneratedMarkedImages value={pre_generated}", file=sys.stderr)

    if pre_generated:
        candidate = project_root / "wwwroot" / Path(str(pre_generated).lstrip("/"))
        if candidate.exists():
            print(f"Using pre-generated marked image. section=Appendix4 path={candidate}", file=sys.stderr)
            map_image = candidate
        else:
            print(f"Marked image missing, using fallback. section=Appendix4 path={candidate}", file=sys.stderr)
    if not source_pdf.exists():
        return []

    reader = PdfReader(str(source_pdf))
    page_index = max(int(page_number) - 1, 0)

    if page_index >= len(reader.pages):
        return []

    if not map_image.exists():
        generated_dir = (
            project_root
            / "wwwroot"
            / "reports"
            / "ihm"
            / str(job_id)
            / "generated-images"
        )

        latest_generated = sorted(
            generated_dir.glob("appendix4-ga-marker-map-*.png"),
            key=lambda path: path.stat().st_mtime,
            reverse=True
        ) if generated_dir.exists() else []

        if latest_generated:
            map_image = latest_generated[0]
            print(
                f"Using latest generated Appendix4 marked image fallback path={map_image}",
                file=sys.stderr
            )
    pages = []

    report_folder = (
        project_root
        / "wwwroot"
        / "reports"
        / "ihm"
        / str(job_id)
    )

    generated_folder = report_folder / "generated-images"
    manifest_path = generated_folder / "appendix4-layout-pages.json"
    area_images = []

    if manifest_path.exists():
        try:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            for item in manifest:
                if item.get("fileName"):
                    candidate = generated_folder / item["fileName"]
                    if candidate.exists():
                        area_images.append(candidate)
        except Exception as exc:
            print(f"Appendix 4 manifest read failed: {exc}", file=sys.stderr)

    if not area_images:
        print("Pre-generated Appendix 4 area images missing. Skipping PDF-time fallback.", file=sys.stderr)
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
    print(f"Appendix4 total internal elapsed_ms={int((time.perf_counter() - section_started) * 1000)}", file=sys.stderr)
    return pages
