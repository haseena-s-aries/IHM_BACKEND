import sys
import json
import cv2
import numpy as np
import os
import re

def normalize_label(value):
    text = str(value or "").upper().strip()
    text = text.replace(" ", "").replace("_", "").replace("-", "")
    text = text.replace("O", "0")
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
            if w < 25 or h < 12:
                continue
            if w > 300 or h > 100:
                continue

            aspect = w / max(h, 1)
            if aspect < 1.2 or aspect > 5.5:
                continue

            boxes.append((x, y, w, h))
            debug_log(f"BOX_ACCEPTED x={x}, y={y}, w={w}, h={h}, aspect={aspect}")
        for x, y, w, h in boxes:
            # Crop inside the blue box border, not the connector line.
            pad = 10
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

            psm_groups = [[7, 8], [6, 11, 13]]
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
                    matches = re.findall(r"[SV]\d{1,2}", label)
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