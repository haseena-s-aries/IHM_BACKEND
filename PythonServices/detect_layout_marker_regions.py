import sys
import json
import cv2
import numpy as np
import os
import re
import traceback
from datetime import datetime
from pathlib import Path
GLOBAL_DEBUG_LOG = Path(
    r"C:\inetpub\wwwroot\Survey\wwwroot\reports\ihm\ocr_script_startup.txt"
)


def global_debug_log(message):
    """Write every OCR diagnostic message into one common server log file."""
    line = f"{datetime.now():%Y-%m-%d %H:%M:%S} | {message}"

    try:
        GLOBAL_DEBUG_LOG.parent.mkdir(parents=True, exist_ok=True)

        with GLOBAL_DEBUG_LOG.open("a", encoding="utf-8") as log_file:
            log_file.write(line + "\n")
            log_file.flush()

    except Exception as log_error:
        print(
            f"OCR LOG WRITE ERROR | "
            f"PATH={GLOBAL_DEBUG_LOG} | "
            f"ERROR={repr(log_error)}",
            file=sys.stderr,
            flush=True
        )


global_debug_log(
    "OCR MODULE LOADED | "
    f"FILE={Path(__file__).resolve()} | "
    f"PYTHON={sys.executable} | "
    f"ARGS={sys.argv} | "
    f"CWD={os.getcwd()}"
)


def normalize_label(value):
    text = str(value or "").upper().strip()
    text = text.replace(" ", "").replace("_", "").replace("-", "")
    # Only normalize OCR mistakes after S/V prefix. Do not change every O in normal words.
    if len(text) >= 2 and text[0] in ("S", "V"):
        text = text[0] + text[1:].replace("O", "0").replace("I", "1").replace("L", "1")
    return text

def detect_regions(image_path):
    image_path_obj = Path(image_path)

    def debug_log(message):
        global_debug_log(message)

    debug_log("===================================")
    debug_log("START Auto Map OCR")
    debug_log(f"IMAGE_PATH={image_path}")
    debug_log(f"CWD={os.getcwd()}")
    debug_log(f"PYTHON={sys.executable}")
    debug_log(f"IMAGE_EXISTS={os.path.exists(image_path)}")
    debug_log(f"IMAGE_FILE_SIZE={os.path.getsize(image_path) if os.path.exists(image_path) else 0}")

    debug_log("START cv2.imread")
    img = cv2.imread(image_path)
    debug_log(f"END cv2.imread | IMAGE_LOADED={img is not None}")

    if img is None:
        debug_log("STOP Auto Map OCR. Reason=cv2.imread returned None")
        debug_log("END Auto Map OCR")
        return []

    original_h, original_w = img.shape[:2]
    detected = {}

    debug_log("\n===================================")
    debug_log(f"IMAGE_PATH={image_path}")
    debug_log(f"IMAGE_SIZE={original_w}x{original_h}")

    try:
        debug_log("START import pytesseract")
        import pytesseract
        debug_log(
            f"END import pytesseract | "
            f"PYTESSERACT_VERSION={getattr(pytesseract, '__version__', 'unknown')}"
        )

        tesseract_path = r"C:\Users\Administrator\AppData\Local\Programs\Tesseract-OCR\tesseract.exe"
        debug_log(
            f"TESSERACT_PATH={tesseract_path} | "
            f"TESSERACT_EXISTS={os.path.exists(tesseract_path)}"
        )

        if os.path.exists(tesseract_path):
            pytesseract.pytesseract.tesseract_cmd = tesseract_path
            debug_log("TESSERACT_COMMAND_CONFIGURED")
        else:
            debug_log("TESSERACT_EXECUTABLE_NOT_FOUND")

        try:
            debug_log(
                f"TESSERACT_VERSION={pytesseract.get_tesseract_version()}"
            )
        except Exception as version_error:
            debug_log(
                f"TESSERACT_VERSION_ERROR={repr(version_error)}"
            )

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

        debug_log("START findContours")
        contours, _ = cv2.findContours(
            label_mask,
            cv2.RETR_EXTERNAL,
            cv2.CHAIN_APPROX_SIMPLE
        )
        debug_log(f"END findContours | CONTOURS_FOUND={len(contours)}")
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
        debug_log(f"ACCEPTED_BOX_COUNT={len(boxes)}")
        debug_log("START BLUE BOX OCR")
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
                    debug_log(
                        f"START BOX OCR | x={x}, y={y}, w={w}, h={h}, PSM={psm}"
                    )
                    raw = pytesseract.image_to_string(
                        crop_gray,
                        config=(
                            f"--psm {psm} --oem 3 "
                            "-c tessedit_char_whitelist=ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 "
                        ),
                        timeout=3
                    )

                    label = normalize_label(raw)
                    debug_log(
                        f"END BOX OCR | x={x}, y={y}, w={w}, h={h}, "
                        f"PSM={psm}, RAW={raw.strip()}, LABEL={label}"
                    )
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
        debug_log("END BLUE BOX OCR")
        debug_log("START FULL IMAGE OCR FALLBACK")
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

            debug_log("START pytesseract.image_to_data")
            ocr_data = pytesseract.image_to_data(
                full_gray,
                config=(
                    "--psm 11 --oem 3 "
                    "-c tessedit_char_whitelist=SVsv0123456789OILoil "
                ),
                output_type=pytesseract.Output.DICT,
                timeout=8
            )

            debug_log(
                f"END pytesseract.image_to_data | "
                f"WORD_COUNT={len(ocr_data.get('text', []))}"
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

            debug_log("END FULL IMAGE OCR FALLBACK")
        except Exception as fallback_ex:
            debug_log(f"FULL_IMAGE_OCR_FALLBACK_ERROR: {repr(fallback_ex)}")
            debug_log(traceback.format_exc())
        debug_log(f"BOXES_FOUND={len(boxes)}")
        debug_log(f"DETECTED={list(detected.keys())}")
        debug_log("========== OCR END ==========")
        return list(detected.values())

    except Exception as ex:
        debug_log(f"OCR_ERROR: {repr(ex)}")
        debug_log(traceback.format_exc())
        print(f"OCR_ERROR: {ex}", file=sys.stderr, flush=True)
        return list(detected.values())

if __name__ == "__main__":
    try:
        global_debug_log("===================================")
        global_debug_log("MAIN START")
        global_debug_log(f"FILE={Path(__file__).resolve()}")
        global_debug_log(f"PYTHON={sys.executable}")
        global_debug_log(f"CWD={os.getcwd()}")
        global_debug_log(f"ARGS={sys.argv}")

        if len(sys.argv) < 2:
            raise ValueError(
                f"Image path argument missing. Received arguments: {sys.argv}"
            )

        image_path = sys.argv[1]

        global_debug_log(f"IMAGE_ARGUMENT={repr(image_path)}")
        global_debug_log(f"IMAGE_ARGUMENT_LENGTH={len(image_path)}")
        global_debug_log(
            f"IMAGE_EXISTS_BEFORE_OCR={Path(image_path).exists()}"
        )

        if Path(image_path).exists():
            global_debug_log(
                f"IMAGE_SIZE_BEFORE_OCR={Path(image_path).stat().st_size}"
            )

        global_debug_log("CALLING detect_regions")
        regions = detect_regions(image_path)
        global_debug_log(
            f"detect_regions RETURNED | COUNT={len(regions)}"
        )

        result_json = json.dumps(regions)
        print(result_json, flush=True)

        global_debug_log(
            f"JSON OUTPUT WRITTEN TO STDOUT | LENGTH={len(result_json)}"
        )
        global_debug_log(
            f"MAIN END | REGIONS={len(regions)}"
        )

    except Exception:
        error_text = traceback.format_exc()

        global_debug_log("MAIN ERROR")
        global_debug_log(error_text)

        print(error_text, file=sys.stderr, flush=True)
        raise