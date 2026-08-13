import json
import sys
from pathlib import Path

from report_pdf.hazardous_materials import build_i2_hazard_layout_images


def main():
    if len(sys.argv) != 4:
        raise SystemExit(
            "Usage: generate_hazardous_marked_images.py <input-json> <output-dir> <manifest-json>"
        )

    input_json = Path(sys.argv[1])
    output_dir = Path(sys.argv[2])
    manifest_json = Path(sys.argv[3])

    data = json.loads(input_json.read_text(encoding="utf-8"))
    output_dir.mkdir(parents=True, exist_ok=True)

    images = build_i2_hazard_layout_images(data, output_dir=output_dir)
    images = [str(Path(image).resolve()) for image in images if Path(image).exists()]

    if not images:
        raise RuntimeError(
            "No hazardous I-2 layout images were generated. Upload an area layout for the hazardous checkpoint area before generating the PDF."
        )

    manifest_json.write_text(
        json.dumps({"images": images}, indent=2),
        encoding="utf-8"
    )

    print(f"Generated hazardous manifest: {manifest_json} images={len(images)}")


if __name__ == "__main__":
    main()
