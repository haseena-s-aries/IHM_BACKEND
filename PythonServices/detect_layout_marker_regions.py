import sys
import json
import cv2
import numpy as np
import os
import re

def detect_regions(image_path):
    img = cv2.imread(image_path)
    if img is None:
        return []

    original_h, original_w = img.shape[:2]

    # Enlarge strongly because layout marker text is usually tiny.
    scale = 3
    big = cv2.resize(img, None, fx=scale, fy=scale, interpolation=cv2.INTER_CUBIC)

    gray = cv2.cvtColor(big, cv2.COLOR_BGR2GRAY)

    # Improve local contrast.
    clahe = cv2.createCLAHE(clipLimit=3.0, tileGridSize=(8, 8))
    gray = clahe.apply(gray)

    candidates = []

    def add_candidates(mask, source_name):
        # Join characters like S01 / V01 into one blob.
        kernels = [
            cv2.getStructuringElement(cv2.MORPH_RECT, (11, 3)),
            cv2.getStructuringElement(cv2.MORPH_RECT, (15, 5)),
            cv2.getStructuringElement(cv2.MORPH_RECT, (21, 7)),
        ]

        for kernel in kernels:
            joined = cv2.dilate(mask, kernel, iterations=1)
            contours, _ = cv2.findContours(
                joined,
                cv2.RETR_EXTERNAL,
                cv2.CHAIN_APPROX_SIMPLE
            )

            for c in contours:
                x, y, w, h = cv2.boundingRect(c)

                ox = x / scale
                oy = y / scale
                ow = w / scale
                oh = h / scale

                # Less strict than before. Old marker text can be small or boxed.
                if ow < 5 or oh < 4:
                    continue
                if ow > original_w * 0.18 or oh > original_h * 0.10:
                    continue

                aspect = ow / max(oh, 1)
                if aspect < 0.25 or aspect > 12:
                    continue

                area = ow * oh
                if area < 25:
                    continue

                crop = img[
                    max(0, int(oy) - 8):min(original_h, int(oy + oh) + 8),
                    max(0, int(ox) - 8):min(original_w, int(ox + ow) + 8)
                ]

                text = ""
                try:
                    import pytesseract
                    tesseract_path = r"C:\Program Files\Tesseract-OCR\tesseract.exe"
                    if os.path.exists(tesseract_path):
                        pytesseract.pytesseract.tesseract_cmd = tesseract_path
                    crop_big = cv2.resize(crop, None, fx=4, fy=4, interpolation=cv2.INTER_CUBIC)
                    crop_gray = cv2.cvtColor(crop_big, cv2.COLOR_BGR2GRAY)
                    crop_gray = cv2.GaussianBlur(crop_gray, (3, 3), 0)
                    crop_gray = cv2.threshold(
                        crop_gray,
                        0,
                        255,
                        cv2.THRESH_BINARY + cv2.THRESH_OTSU
                    )[1]

                    if np.mean(crop_gray) < 127:
                        crop_gray = cv2.bitwise_not(crop_gray)

                    ocr_results = []

                    for psm in [7, 8]:
                        raw = pytesseract.image_to_string(
                            crop_gray,
                            config=f"--psm {psm} --oem 3 -c tessedit_char_whitelist=SVvOo0123456789-_",
                            timeout=3
                        )
                        cleaned = raw.strip().upper().replace(" ", "").replace("\n", "").replace("_", "").replace("-", "")
                        cleaned = cleaned.replace("O", "0")

                        if cleaned:
                            ocr_results.append(cleaned)

                    text = ocr_results[0] if ocr_results else ""
                except Exception as ex:
                    text = ""
                    print(f"OCR_ERROR: {ex}", file=sys.stderr)
                candidates.append({
                    "xPercent": (ox + ow / 2) / original_w,
                    "yPercent": (oy + oh / 2) / original_h,
                    "widthPercent": ow / original_w,
                    "heightPercent": oh / original_h,
                    "confidence": 0.55,
                    "source": source_name,
                    "text": text
                })

    # Method 1: dark text.
    blur = cv2.GaussianBlur(gray, (3, 3), 0)
    dark = cv2.adaptiveThreshold(
        blur,
        255,
        cv2.ADAPTIVE_THRESH_GAUSSIAN_C,
        cv2.THRESH_BINARY_INV,
        31,
        7
    )
    add_candidates(dark, "adaptive_dark")

    # Method 2: global dark threshold.
    _, global_dark = cv2.threshold(gray, 170, 255, cv2.THRESH_BINARY_INV)
    add_candidates(global_dark, "global_dark")

    # Method 3: edge based, catches boxed/outlined labels.
    edges = cv2.Canny(gray, 60, 180)
    add_candidates(edges, "edges")

    # Method 4: red/blue/colored marker text or boxes.
    hsv = cv2.cvtColor(big, cv2.COLOR_BGR2HSV)

    # red has two HSV ranges
    red1 = cv2.inRange(hsv, np.array([0, 35, 40]), np.array([15, 255, 255]))
    red2 = cv2.inRange(hsv, np.array([160, 35, 40]), np.array([180, 255, 255]))
    red = cv2.bitwise_or(red1, red2)
    add_candidates(red, "red")

    blue = cv2.inRange(hsv, np.array([85, 35, 40]), np.array([135, 255, 255]))
    add_candidates(blue, "blue")

    # Deduplicate nearby boxes.
    unique = []
    for r in sorted(candidates, key=lambda v: (v["yPercent"], v["xPercent"])):
        duplicate = False
        for u in unique:
            dx = abs(r["xPercent"] - u["xPercent"])
            dy = abs(r["yPercent"] - u["yPercent"])
            if dx < 0.018 and dy < 0.018:
                duplicate = True
                # Keep larger/more confident candidate.
                if r["widthPercent"] * r["heightPercent"] > u["widthPercent"] * u["heightPercent"]:
                    u.update(r)
                break

        if not duplicate:
            unique.append(r)

    # Filter out very top/bottom page noise if needed, but keep broad by default.
    unique = [
        r for r in unique
        if 0 <= r["xPercent"] <= 1 and 0 <= r["yPercent"] <= 1
    ]

    unique.sort(key=lambda r: (round(r["yPercent"], 2), r["xPercent"]))

    return unique[:80]

if __name__ == "__main__":
    image_path = sys.argv[1]
    regions = detect_regions(image_path)
    print(json.dumps(regions))