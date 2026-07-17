import sys
import json
import cv2
import numpy as np
import os
import re

def normalize_label(value):
    text = str(value or "").upper().strip()
    text = text.replace(" ", "").replace("_", "").replace("-", "")
    # Only normalize OCR mistakes after S/V prefix. Do not change every O in normal words.
    if len(text) >= 2 and text[0] in ("S", "V"):
        text = text[0] + text[1:].replace("O", "0").replace("I", "1").replace("L", "1")
    return text

def detect_regions(image_path):
    img = cv2.imread(image_path)
    if img is None:
        return []

    original_h, original_w = img.shape[:2]
    detected = {}
    DEBUG_FILE = r"E:\ARIES_PROJECTS\AurasApp_Apis\AurasApp_Apis\AurasApp_Apis\wwwroot\reports\ihm\ocr_debug.txt"

    def debug_log(message):
        try:
            os.makedirs(os.path.dirname(DEBUG_FILE), exist_ok=True)
            with open(DEBUG_FILE, "a", encoding="utf-8") as f:
                f.write(str(message) + "\n")
        except Exception:
            pass

    debug_log("\n===================================")
    debug_log(f"IMAGE_PATH={image_path}")
    debug_log(f"IMAGE_SIZE={original_w}x{original_h}")

    try:
        import pytesseract

        tesseract_path = r"C:\Program Files\Tesseract-OCR\tesseract.exe"
        if os.path.exists(tesseract_path):
            pytesseract.pytesseract.tesseract_cmd = tesseract_path

        hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)
        debug_log("Image loaded and HSV converted")
        blue_mask = cv2.inRange(
            hsv,
            np.array([85, 35, 40]),
            np.array([140, 255, 255])
        )
        debug_log(f"BLUE_PIXELS={cv2.countNonZero(blue_mask)}")
        # Remove long blue connector lines so only label boxes/text remain.
        vertical_kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (1, 45))
        horizontal_kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (45, 1))

        vertical_lines = cv2.morphologyEx(blue_mask, cv2.MORPH_OPEN, vertical_kernel)
        horizontal_lines = cv2.morphologyEx(blue_mask, cv2.MORPH_OPEN, horizontal_kernel)

        line_mask = cv2.bitwise_or(vertical_lines, horizontal_lines)
        label_mask = cv2.subtract(blue_mask, line_mask)

        # Reconnect label box/text pieces.
        join_kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (3, 2))
        label_mask = cv2.dilate(label_mask, join_kernel, iterations=1)

        contours, _ = cv2.findContours(
            label_mask,
            cv2.RETR_EXTERNAL,
            cv2.CHAIN_APPROX_SIMPLE
        )
        debug_log(f"CONTOURS_FOUND={len(contours)}")
        boxes = []

        for contour in contours:
            x, y, w, h = cv2.boundingRect(contour)

            # Keep small blue label rectangles, ignore long connector lines.
            if w < 18 or h < 8:
                continue
            if w > 360 or h > 130:
                continue

            aspect = w / max(h, 1)
            if aspect < 1.2 or aspect > 5.5:
                continue

            boxes.append((x, y, w, h))
            debug_log(f"BOX_ACCEPTED x={x}, y={y}, w={w}, h={h}, aspect={aspect}")
        for x, y, w, h in boxes:
            # Crop inside the blue box border, not the connector line.
            pad = 16
            crop = img[
                max(0, y - pad):min(original_h, y + h + pad),
                max(0, x - pad):min(original_w, x + w + pad)
            ]

            if crop.size == 0:
                continue

            crop_big = cv2.resize(crop, None, fx=8, fy=8, interpolation=cv2.INTER_CUBIC)
            crop_gray = cv2.cvtColor(crop_big, cv2.COLOR_BGR2GRAY)

            crop_gray = cv2.GaussianBlur(crop_gray, (3, 3), 0)
            crop_gray = cv2.threshold(
                crop_gray,
                0,
                255,
                cv2.THRESH_BINARY + cv2.THRESH_OTSU
            )[1]

            psm_groups = [[7, 8, 13], [6, 11]]
            found = False

            for psm_group in psm_groups:
                if found:
                    break

                for psm in psm_group:
                    raw = pytesseract.image_to_string(
                        crop_gray,
                        config=(
                            f"--psm {psm} --oem 3 "
                            "-c tessedit_char_whitelist=ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 "
                        ),
                        timeout=3
                    )

                    label = normalize_label(raw)
                    debug_log(f"BOX x={x}, y={y}, w={w}, h={h}, PSM={psm}, RAW={raw.strip()}, LABEL={label}")
                    matches = re.findall(r"(?<![A-Z0-9])[SV]\d{1,3}(?![A-Z0-9])", label)
                    if not matches:
                        continue

                    for m in matches:
                        text = normalize_label(m)

                        # Convert V2 -> V02, S1 -> S01
                        prefix = text[0]
                        digits = re.sub(r"\D", "", text[1:])
                        if not digits:
                            continue

                        number = int(digits)
                        if number <= 0:
                            continue

                        text = f"{prefix}{number:02d}"

                        detected[text] = {
                            "xPercent": (x + w / 2) / original_w,
                            "yPercent": (y + h / 2) / original_h,
                            "widthPercent": w / original_w,
                            "heightPercent": h / original_h,
                            "confidence": 0.98,
                            "source": "blue_label_box_ocr",
                            "text": text
                        }
                    found = True
                    break
                # Fallback OCR over the full layout. This catches marker labels missed by
        # the blue-box contour pass because of thin borders, broken boxes, or tight crops.
        try:
            full_big = cv2.resize(img, None, fx=3, fy=3, interpolation=cv2.INTER_CUBIC)
            full_gray = cv2.cvtColor(full_big, cv2.COLOR_BGR2GRAY)
            full_gray = cv2.GaussianBlur(full_gray, (3, 3), 0)
            full_gray = cv2.threshold(
                full_gray,
                0,
                255,
                cv2.THRESH_BINARY + cv2.THRESH_OTSU
            )[1]

            ocr_data = pytesseract.image_to_data(
                full_gray,
                config=(
                    "--psm 11 --oem 3 "
                    "-c tessedit_char_whitelist=SVsv0123456789OILoil "
                ),
                output_type=pytesseract.Output.DICT,
                timeout=8
            )

            for i, raw in enumerate(ocr_data.get("text", [])):
                label = normalize_label(raw)
                matches = re.findall(r"[SV]\d{1,3}", label)
                if not matches:
                    continue

                try:
                    confidence = float(ocr_data["conf"][i])
                except Exception:
                    confidence = 0

                if confidence < 25:
                    continue

                x = int(ocr_data["left"][i] / 3)
                y = int(ocr_data["top"][i] / 3)
                w = int(ocr_data["width"][i] / 3)
                h = int(ocr_data["height"][i] / 3)

                if w < 6 or h < 6:
                    continue

                for m in matches:
                    text = normalize_label(m)
                    prefix = text[0]
                    digits = re.sub(r"\D", "", text[1:])
                    if not digits:
                        continue

                    number = int(digits)
                    if number <= 0:
                        continue

                    text = f"{prefix}{number:02d}"

                    if text in detected:
                        continue

                    detected[text] = {
                        "xPercent": (x + w / 2) / original_w,
                        "yPercent": (y + h / 2) / original_h,
                        "widthPercent": max(w, 20) / original_w,
                        "heightPercent": max(h, 12) / original_h,
                        "confidence": max(confidence / 100, 0.55),
                        "source": "full_image_ocr_fallback",
                        "text": text
                    }

                    debug_log(f"FALLBACK_DETECTED text={text}, conf={confidence}, x={x}, y={y}, w={w}, h={h}")

        except Exception as fallback_ex:
            debug_log(f"FULL_IMAGE_OCR_FALLBACK_ERROR: {fallback_ex}")
        debug_log(f"BOXES_FOUND={len(boxes)}")
        debug_log(f"DETECTED={list(detected.keys())}")
        debug_log("========== OCR END ==========")
        return list(detected.values())

    except Exception as ex:
        debug_log(f"OCR_ERROR: {ex}")
        print(f"OCR_ERROR: {ex}", file=sys.stderr)
        return list(detected.values())

if __name__ == "__main__":
    image_path = sys.argv[1]
    regions = detect_regions(image_path)
    print(json.dumps(regions))