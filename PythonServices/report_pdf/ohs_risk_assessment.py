from io import BytesIO

from pypdf import PdfReader
from reportlab.lib import colors
from reportlab.lib.utils import ImageReader
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfgen import canvas

from report_pdf.helpers import txt, format_date, resolve_file_path, wrap_text_to_lines


PAGE_WIDTH = 840
PAGE_HEIGHT = 600

FONT = "Times-Roman"
FONT_BOLD = "Times-Bold"
FONT_SIZE = 9
RISK_FONT_SIZE = 8
TITLE_SIZE = 16

LEFT_X = 28
RIGHT_X = 812
TOP_Y = 570
BOTTOM_Y = 30

CONTENT_LEFT = 32
CONTENT_RIGHT = 808
TITLE_Y = 525
LOGO_BOX = (45, 505, 170, 555)

SIGNATURE_HEIGHT = 72
PAGE_SAFE_BOTTOM = 40
SIGNATURE_HEIGHT = 72

def report_or_default(report, key, fallback):
    return txt(report.get(key) or fallback)


def draw_text_center(c, x1, y1, x2, y2, value, font=FONT, size=FONT_SIZE):
    value = txt(value)
    if not value:
        return

    c.setFont(font, size)
    text_width = pdfmetrics.stringWidth(value, font, size)
    x = x1 + ((x2 - x1) - text_width) / 2
    y = y1 + ((y2 - y1) - size) / 2 + 2
    c.drawString(x, y, value)


def draw_text_left(c, x1, y1, x2, y2, value, font=FONT, size=FONT_SIZE):
    value = txt(value)
    if not value:
        return

    c.setFont(font, size)
    y = y1 + ((y2 - y1) - size) / 2 + 2
    c.drawString(x1 + 4, y, value)


def draw_wrapped_left(c, x1, y1, x2, y2, value, font=FONT, size=RISK_FONT_SIZE, bullet=False):
    value = txt(value)
    if not value:
        return

    max_width = (x2 - x1) - 8
    lines = []

    if bullet:
        for item in split_bullets(value):
            wrapped = wrap_text_to_lines(item, font, size, max_width - 10)
            if wrapped:
                lines.append(("bullet", wrapped))
    else:
        for paragraph in value.splitlines():
            wrapped = wrap_text_to_lines(paragraph, font, size, max_width)
            if wrapped:
                lines.append(("text", wrapped))

    line_height = size + 2
    total_lines = sum(len(block_lines) for _, block_lines in lines)
    total_height = total_lines * line_height
    y = y1 + ((y2 - y1) - total_height) / 2 + total_height - size

    c.setFont(font, size)
    c.setFillColor(colors.black)

    for block_type, block_lines in lines:
        for index, line in enumerate(block_lines):
            if block_type == "bullet":
                if index == 0:
                    c.drawString(x1 + 4, y, "\u2022")
                c.drawString(x1 + 14, y, line)
            else:
                c.drawString(x1 + 4, y, line)
            y -= line_height


def split_bullets(value):
    lines = []

    for raw_line in txt(value).splitlines():
        item = raw_line.strip()
        if not item:
            continue
        if item.startswith("-"):
            item = item[1:].strip()
        if item.startswith("\u2022") or item.startswith("\x95"):
            item = item[1:].strip()
        lines.append(item)

    return lines or [txt(value)]


def draw_cell(c, x1, y1, x2, y2, value="", font=FONT, size=FONT_SIZE, fill=None, align="center", bullet=False):
    c.setStrokeColor(colors.black)
    c.setLineWidth(0.3)

    if fill:
        c.setFillColor(fill)
        c.rect(x1, y1, x2 - x1, y2 - y1, fill=1, stroke=1)
        c.setFillColor(colors.black)
    else:
        c.rect(x1, y1, x2 - x1, y2 - y1)

    if bullet:
        draw_wrapped_left(c, x1, y1, x2, y2, value, font, size, bullet=True)
    elif align == "left":
        draw_wrapped_left(c, x1, y1, x2, y2, value, font, size)
    else:
        draw_text_center(c, x1, y1, x2, y2, value, font, size)


def draw_logo(c):
    logo_path = resolve_file_path("wwwroot/images/Aries_new.png")
    if not logo_path:
        logo_path = resolve_file_path("/images/Aries_new.png")
    if not logo_path:
        return

    img = ImageReader(logo_path)
    img_w, img_h = img.getSize()

    box_x1, box_y1, box_x2, box_y2 = LOGO_BOX
    scale = min((box_x2 - box_x1) / img_w, (box_y2 - box_y1) / img_h)
    final_w = img_w * scale
    final_h = img_h * scale

    x = box_x1 + ((box_x2 - box_x1) - final_w) / 2
    y = box_y1 + ((box_y2 - box_y1) - final_h) / 2

    c.drawImage(img, x, y, final_w, final_h, preserveAspectRatio=True, mask="auto")


def start_ohs_page(c):
    c.setPageSize((PAGE_WIDTH, PAGE_HEIGHT))
    c.setStrokeColor(colors.black)
    c.setLineWidth(1)
    c.rect(LEFT_X, BOTTOM_Y, RIGHT_X - LEFT_X, TOP_Y - BOTTOM_Y)

    draw_logo(c)

    c.setFont(FONT_BOLD, TITLE_SIZE)
    title = "Occupational Health and Safety (OHS) Risk Assessment"
    title_width = pdfmetrics.stringWidth(title, FONT_BOLD, TITLE_SIZE)
    c.drawString((PAGE_WIDTH - title_width) / 2, TITLE_Y, title)


def get_people(data, role_type, count):
    people = data.get("ohsPeople") or []

    values = [
        txt(x.get("name"))
        for x in people
        if isinstance(x, dict)
        and txt(x.get("roleType")).lower() == role_type.lower()
        and txt(x.get("name"))
    ]

    return values[:count]


def numbered_value(values, index):
    value = values[index] if index < len(values) else ""
    return f"{index + 1}. {value}" if value else f"{index + 1}."


def build_ohs_values(data):
    job = data.get("job") or {}
    report = data.get("reportInfo") or data.get("report") or {}

    return {
        "jobDetail": report_or_default(report, "ohsJobDetail", job.get("projectName")),
        "client": report_or_default(report, "ohsClient", job.get("clientName")),
        "jobLocation": report_or_default(report, "ohsJobLocation", job.get("auditPort")),
        "jobNo": report_or_default(report, "ohsJobNo", job.get("jobNumber")),
        "projectInCharge": report_or_default(report, "ohsProjectInCharge", report.get("hazmatExpert")),
        "divisionBranch": report_or_default(report, "ohsDivisionBranch", report.get("divisionBranch")),
        "raNo": report_or_default(report, "ohsRaNo", report.get("raNo")),
        "revNo": report_or_default(report, "ohsRevNo", report.get("revisionNo")),
        "revDate": format_date(report_or_default(report, "ohsRevisionDate", report.get("revisionDate"))),
        "hseOfficers": get_people(data, "HseOfficer", 3),
        "responsibleSupervisors": get_people(data, "ResponsibleSupervisor", 6),
        "participants": get_people(data, "Participant", 12),
    }


def draw_ohs_summary_table(c, data):
    values = build_ohs_values(data)
    table_x1 = CONTENT_LEFT
    table_x2 = CONTENT_RIGHT
    table_top = 492
    row_h = 24
    gray = colors.HexColor("#D4D0D0")

    x = [table_x1, 140, 355, 470, 615, 690, table_x2]
    y = table_top

    rows = [
        ("Job Detail:", values["jobDetail"], "Job No.:", values["jobNo"], "RA No.:", values["raNo"]),
        ("Client:", values["client"], "Project In-Charge:", values["projectInCharge"], "Rev No.:", values["revNo"]),
        ("Job Location:", values["jobLocation"], "Division / Branch:", values["divisionBranch"], "Rev Date:", values["revDate"]),
    ]

    for label1, value1, label2, value2, label3, value3 in rows:
        y2 = y - row_h
        draw_cell(c, x[0], y2, x[1], y, label1, FONT_BOLD, FONT_SIZE, gray, "left")
        draw_cell(c, x[1], y2, x[2], y, value1, FONT_BOLD, FONT_SIZE)
        draw_cell(c, x[2], y2, x[3], y, label2, FONT_BOLD, FONT_SIZE, gray, "left")
        draw_cell(c, x[3], y2, x[4], y, value2, FONT_BOLD, FONT_SIZE)
        draw_cell(c, x[4], y2, x[5], y, label3, FONT_BOLD, FONT_SIZE, gray, "left")
        draw_cell(c, x[5], y2, x[6], y, value3, FONT_BOLD, FONT_SIZE)
        y = y2

    header_h = 18
    y2 = y - header_h
    p = [table_x1, 140, 355, table_x2]

    draw_cell(c, p[0], y2, p[1], y, "HSE Officer", FONT_BOLD, FONT_SIZE, gray, "left")
    draw_cell(c, p[1], y2, p[2], y, "Responsible Supervisor / Foreman", FONT_BOLD, FONT_SIZE, gray, "left")
    draw_cell(c, p[2], y2, p[3], y, "Participants", FONT_BOLD, FONT_SIZE, gray, "left")

    y = y2
    small_row_h = 18

    hse = values["hseOfficers"]
    supervisors = values["responsibleSupervisors"]
    participants = values["participants"]

    for row_index in range(3):
        y2 = y - small_row_h
        draw_cell(c, p[0], y2, p[1], y, numbered_value(hse, row_index), FONT, FONT_SIZE, align="left")

        sup_mid = p[1] + ((p[2] - p[1]) / 2)
        draw_cell(c, p[1], y2, sup_mid, y, numbered_value(supervisors, row_index), FONT, FONT_SIZE, align="left")
        draw_cell(c, sup_mid, y2, p[2], y, numbered_value(supervisors, row_index + 3), FONT, FONT_SIZE, align="left")

        participant_width = (p[3] - p[2]) / 4
        for col in range(4):
            idx = row_index + (col * 3)
            x1 = p[2] + (participant_width * col)
            x2 = x1 + participant_width
            draw_cell(c, x1, y2, x2, y, numbered_value(participants, idx), FONT, FONT_SIZE, align="left")

        y = y2

    return y


def risk_rows(data):
    return [
        {
            "activity": "Conduct Survey and Sampling",
            "hazardDescription": "Release of Hazardous materials during sampling.",
            "risks": "Personnel injuries\nInhalation of Hazardous Materials",
            "initialRiskP": "3",
            "initialRiskS": "D",
            "initialRiskR": "3D",
            "initialAlarp": "High",
            "controlMeasures": "Ensure the works are being carried under close Supervision\nEnsure competent person for the assignment.\nVisual inspections on materials surface conditions before sampling\nMust wear appropriate PPE (Hand gloves, Googles etc)",
            "residualRiskP": "1",
            "residualRiskS": "D",
            "residualRiskR": "1D",
            "residualAlarp": "Medium",
            "responsiblePerson": "Aries Surveyor/Engineer & Client representative (Project Manager)",
        },
        {
            "activity": "Packing of Samples",
            "hazardDescription": "Release of Hazardous Materials",
            "risks": "Life threatening Disease",
            "initialRiskP": "3",
            "initialRiskS": "D",
            "initialRiskR": "3D",
            "initialAlarp": "High",
            "controlMeasures": "Properly seal all Hazardous Materials Sample Bag",
            "residualRiskP": "1",
            "residualRiskS": "D",
            "residualRiskR": "1D",
            "residualAlarp": "Medium",
            "responsiblePerson": "Aries Surveyor/Engineer & Client representative (Project Manager)",
        },
        {
            "activity": "Heat Stress (working in extreme temperature)",
            "hazardDescription": "Heat Stroke,\nHeat Exhaustion,\nHeat Syncope,\nHeat Cramps,\nHeat Rash",
            "risks": "Personnel injuries.",
            "initialRiskP": "4",
            "initialRiskS": "D",
            "initialRiskR": "4D",
            "initialAlarp": "High",
            "controlMeasures": "Know signs/symptoms of heat-related illnesses and monitor yourself and coworkers.\nBlock or stay out of direct sunlight or other heat sources.\nUse cooling fans or air-conditioning and rest regularly.\nDrink lots of water, at least 1 cup every 15 minutes.\nWear lightweight, light colored, loose-fitting clothes.\nAvoid alcohol, caffeinated drinks, or heavy meals.\nCall immediately site emergency number / supervisor and report if any incident occur.\nMove the victim to a cool, shaded area, loosen or remove heavy clothing. Provide cool drinking water, fan and mist the person with water.",
            "residualRiskP": "1",
            "residualRiskS": "D",
            "residualRiskR": "1D",
            "residualAlarp": "Medium",
            "responsiblePerson": "Aries Surveyor/Engineer & Client representative (Project Manager)",
        },
        {
            "activity": "Mobilization to the work area",
            "hazardDescription": "Inadequate knowledge of site safety rules & regulation",
            "risks": "Property Damage\nPersonnel Injury",
            "initialRiskP": "2",
            "initialRiskS": "C",
            "initialRiskR": "2C",
            "initialAlarp": "High",
            "controlMeasures": "Ensure Personnel are safety inducted and Competent for the Job\nEnsure the works are being carried under close Supervision.\nEnsure work area in good condition with adequate maintenance.\nProvide adequate information about site location for mobilization.\nEnsure engineer competent for the assignment\nEnsure Inspection equipment / tools specification meeting as per client requirements.",
            "residualRiskP": "1",
            "residualRiskS": "B",
            "residualRiskR": "2B",
            "residualAlarp": "Low",
            "responsiblePerson": "Aries Surveyor/Engineer & Client representative (Project Manager)",
        },
        {
            "activity": "Working at heights",
            "hazardDescription": "Inadequate fall protection.\nSlippery floor.\nInadequate access.\nDefective ladders.",
            "risks": "Personnel injuries\nFatality",
            "initialRiskP": "4",
            "initialRiskS": "D",
            "initialRiskR": "4D",
            "initialAlarp": "High",
            "controlMeasures": "Ensure platform is safe and secure.\nWhile working more than 2meters all persons should wear full body harness.\nEnsure ladder condition is good and that are using at correct angle.\nEnsure there are no overhead lines near working at heights and ladder\n100% tie off when working in heights.\nConsideration shall be given to environmental conditions prior to starting the work.\nNo work to be undertaken in adverse weather conditions (wind, rain etc.)",
            "residualRiskP": "1",
            "residualRiskS": "D",
            "residualRiskR": "1D",
            "residualAlarp": "Medium",
            "responsiblePerson": "Aries Surveyor/Engineer & Client representative (Project Manager)",
        },
        {
            "activity": "Work in Confined Space",
            "hazardDescription": "Dust\nFumes\nPoisonous Gases / Toxic Gases\nH2S\nHazardous Chemicals\nO2 deficiency\nSlip and fall\nLess illumination\nFalling Objects\nPresence of Insects, Spider etc.\nFire Explosion",
            "risks": "Fatality\nPersonnel injuries",
            "initialRiskP": "4",
            "initialRiskS": "D",
            "initialRiskR": "4D",
            "initialAlarp": "High",
            "controlMeasures": "Ensure PTW approved by concern authorities to perform the task and strictly followed the PTW system\nEnsure Tool Box Talk conducted prior to start the task\nOnly competent personnel allowed to enter in confined space.\nGas & Oxygen testing shall be done prior to the commencement of work.\nLOG in/LOG out attendance sheet should be maintained where confined space activities are going on.\nAdequate ventilation & lighting must be provided\nStandby watch man with high visibility vest should be outside the man-way all the times while personnel inside the tank.\nPersonnel should carry multiple gas detector all the time when inside confined space\nTwo (2) way communication system shall be in place for effective communication\nPersonnel shall take frequent break.\nAppropriate PPE has to be available and worn.\nRemove all the loose material if hanging on the top or on the edges",
            "residualRiskP": "1",
            "residualRiskS": "D",
            "residualRiskR": "1D",
            "residualAlarp": "Medium",
            "responsiblePerson": "Aries Surveyor/Engineer & Client representative (Safety Officer)",
        },
        {
            "activity": "Work in enclosed/semi enclosed hazardous space",
            "hazardDescription": "Poisonous Gases / Toxic Gases\nHazardous liquids\nO2 deficiency\nSlip and fall\nFire Explosion",
            "risks": "Fatality\nPersonnel injuries",
            "initialRiskP": "4",
            "initialRiskS": "D",
            "initialRiskR": "4D",
            "initialAlarp": "High",
            "controlMeasures": "Ensure Tool Box Talk conducted prior to start the task\nPersonnel should carry multiple gas detector all the time when inside hazardous space\nEscape set to be kept standby at the entrance of hazardous space\nContinuous light & ventilation to be available in the hazardous space",
            "residualRiskP": "1",
            "residualRiskS": "D",
            "residualRiskR": "1D",
            "residualAlarp": "Medium",
            "responsiblePerson": "Aries Surveyor/Engineer & Client representative (Safety Officer)",
        },
        {
            "activity": "Site inspection",
            "hazardDescription": "Falling objects\nNoise\nSlip & trip",
            "risks": "Property damage\nPersonnel injuries\nDangerous Occurrence",
            "initialRiskP": "4",
            "initialRiskS": "C",
            "initialRiskR": "4C",
            "initialAlarp": "High",
            "controlMeasures": "Proper head gear to be worn to protect the head\nEar plugs to be used in noisy areas\nProper PPE to be worn all the time while working at site\nPersonnel should keep an eye on the surroundings he/she is working in to observe any kind of hazard around them",
            "residualRiskP": "1",
            "residualRiskS": "C",
            "residualRiskR": "1C",
            "residualAlarp": "LOW",
            "responsiblePerson": "Aries Surveyor/Engineer & Client representative (Project Manager)",
        },
    ]


def risk_value(row, key):
    return txt(row.get(key)) if isinstance(row, dict) else ""

def wrap_lines(value, width, size=RISK_FONT_SIZE, bullet=False):
    if bullet:
        lines = []
        for item in split_bullets(value):
            wrapped = wrap_text_to_lines(item, FONT, size, width - 18)
            for index, line in enumerate(wrapped):
                lines.append(("bullet-first" if index == 0 else "bullet-cont", line))
        return lines or [("text", "-")]

    lines = []
    for paragraph in txt(value).splitlines() or [""]:
        for line in wrap_text_to_lines(paragraph, FONT, size, width - 8):
            lines.append(("text", line))
    return lines or [("text", "-")]


def build_risk_line_blocks(row, xs):
    return {
        "activity": wrap_lines(risk_value(row, "activity"), xs[1] - xs[0]),
        "hazardDescription": wrap_lines(risk_value(row, "hazardDescription"), xs[2] - xs[1]),
        "risks": wrap_lines(risk_value(row, "risks"), xs[3] - xs[2]),
        "initialRiskP": wrap_lines(risk_value(row, "initialRiskP"), xs[4] - xs[3]),
        "initialRiskS": wrap_lines(risk_value(row, "initialRiskS"), xs[5] - xs[4]),
        "initialRiskR": wrap_lines(risk_value(row, "initialRiskR"), xs[6] - xs[5]),
        "initialAlarp": wrap_lines(risk_value(row, "initialAlarp"), xs[7] - xs[6]),
        "controlMeasures": wrap_lines(risk_value(row, "controlMeasures"), xs[8] - xs[7], bullet=True),
        "residualRiskP": wrap_lines(risk_value(row, "residualRiskP"), xs[9] - xs[8]),
        "residualRiskS": wrap_lines(risk_value(row, "residualRiskS"), xs[10] - xs[9]),
        "residualRiskR": wrap_lines(risk_value(row, "residualRiskR"), xs[11] - xs[10]),
        "residualAlarp": wrap_lines(risk_value(row, "residualAlarp"), xs[12] - xs[11]),
        "responsiblePerson": wrap_lines(risk_value(row, "responsiblePerson"), xs[13] - xs[12]),
    }


def remaining_line_count(blocks):
    return max((len(lines) for lines in blocks.values()), default=1)


def take_risk_line_blocks(blocks, max_lines):
    current = {}
    rest = {}

    for key, lines in blocks.items():
        current[key] = lines[:max_lines]
        rest[key] = lines[max_lines:]

    return current, rest


def has_remaining_blocks(blocks):
    return any(lines for lines in blocks.values())


def draw_line_items(c, x1, y1, x2, y2, line_items, size=RISK_FONT_SIZE):
    line_height = size + 2
    total_height = len(line_items) * line_height
    y = y1 + ((y2 - y1) - total_height) / 2 + total_height - size

    c.setFont(FONT, size)
    c.setFillColor(colors.black)

    for line_type, line in line_items:
        if line_type == "bullet-first":
            c.drawString(x1 + 4, y, "\u2022")
            c.drawString(x1 + 14, y, line)
        elif line_type == "bullet-cont":
            c.drawString(x1 + 14, y, line)
        else:
            c.drawString(x1 + 4, y, line)

        y -= line_height


def draw_split_cell(c, x1, y1, x2, y2, line_items):
    c.setStrokeColor(colors.black)
    c.setLineWidth(0.3)
    c.rect(x1, y1, x2 - x1, y2 - y1)
    draw_line_items(c, x1, y1, x2, y2, line_items)

def draw_split_risk_row(c, y, height, xs, blocks):
    y2 = y - height

    draw_split_cell(c, xs[0], y2, xs[1], y, blocks["activity"])
    draw_split_cell(c, xs[1], y2, xs[2], y, blocks["hazardDescription"])
    draw_split_cell(c, xs[2], y2, xs[3], y, blocks["risks"])

    draw_split_cell(c, xs[3], y2, xs[4], y, blocks["initialRiskP"])
    draw_split_cell(c, xs[4], y2, xs[5], y, blocks["initialRiskS"])
    draw_split_cell(c, xs[5], y2, xs[6], y, blocks["initialRiskR"])
    draw_split_cell(c, xs[6], y2, xs[7], y, blocks["initialAlarp"])

    draw_split_cell(c, xs[7], y2, xs[8], y, blocks["controlMeasures"])

    draw_split_cell(c, xs[8], y2, xs[9], y, blocks["residualRiskP"])
    draw_split_cell(c, xs[9], y2, xs[10], y, blocks["residualRiskS"])
    draw_split_cell(c, xs[10], y2, xs[11], y, blocks["residualRiskR"])
    draw_split_cell(c, xs[11], y2, xs[12], y, blocks["residualAlarp"])
    draw_split_cell(c, xs[12], y2, xs[13], y, blocks["responsiblePerson"])

    return y2

def draw_risk_header(c, y):
    header_top = y
    mid = y - 13
    bottom = y - 26

    xs = [
    CONTENT_LEFT,  # 32
    125,           # Activity
    250,           # Hazard Description
    328,           # Risks
    344,           # Initial P
    360,           # Initial S
    384,           # Initial R
    425,           # ALARP
    640,           # Control measures, reduced
    656,           # Residual P
    672,           # Residual S
    696,           # Residual R
    740,           # Residual ALARP
    CONTENT_RIGHT  # Responsible Person, wider
]

    draw_cell(c, xs[0], bottom, xs[1], header_top, "Activity", size=RISK_FONT_SIZE)
    draw_cell(c, xs[1], bottom, xs[2], header_top, "Hazard Description", size=RISK_FONT_SIZE)
    draw_cell(c, xs[2], bottom, xs[3], header_top, "Risks", size=RISK_FONT_SIZE)

    draw_cell(c, xs[3], mid, xs[6], header_top, "Initial Risk", size=RISK_FONT_SIZE)
    draw_cell(c, xs[3], bottom, xs[4], mid, "P", size=RISK_FONT_SIZE)
    draw_cell(c, xs[4], bottom, xs[5], mid, "S", size=RISK_FONT_SIZE)
    draw_cell(c, xs[5], bottom, xs[6], mid, "R", size=RISK_FONT_SIZE)

    draw_cell(c, xs[6], bottom, xs[7], header_top, "ALARP", size=RISK_FONT_SIZE)
    draw_cell(c, xs[7], bottom, xs[8], header_top, "Control measures", size=RISK_FONT_SIZE)

    draw_cell(c, xs[8], mid, xs[11], header_top, "Residual Risk", size=RISK_FONT_SIZE)
    draw_cell(c, xs[8], bottom, xs[9], mid, "P", size=RISK_FONT_SIZE)
    draw_cell(c, xs[9], bottom, xs[10], mid, "S", size=RISK_FONT_SIZE)
    draw_cell(c, xs[10], bottom, xs[11], mid, "R", size=RISK_FONT_SIZE)

    draw_cell(c, xs[11], bottom, xs[12], header_top, "ALARP", size=RISK_FONT_SIZE)
    draw_cell(c, xs[12], bottom, xs[13], header_top, "Responsible Person", size=RISK_FONT_SIZE)

    return bottom, xs


def line_count(value, width, size=RISK_FONT_SIZE, bullet=False):
    if bullet:
        count = 0
        for item in split_bullets(value):
            count += len(wrap_text_to_lines(item, FONT, size, width - 18))
        return max(1, count)

    count = 0
    for paragraph in txt(value).splitlines() or [""]:
        count += len(wrap_text_to_lines(paragraph, FONT, size, width - 8))
    return max(1, count)


def risk_row_height(row, xs):
    values = [
        ("activity", 0, False),
        ("hazardDescription", 1, False),
        ("risks", 2, False),
        ("controlMeasures", 7, True),
        ("responsiblePerson", 12, False),
    ]

    max_lines = 1
    for key, index, bullet in values:
        max_lines = max(max_lines, line_count(risk_value(row, key), xs[index + 1] - xs[index], RISK_FONT_SIZE, bullet))

    return max(48, (max_lines * (RISK_FONT_SIZE + 2)) + 10)


def draw_risk_row(c, row, y, height, xs):
    y2 = y - height

    draw_cell(c, xs[0], y2, xs[1], y, risk_value(row, "activity"), size=RISK_FONT_SIZE, align="left")
    draw_cell(c, xs[1], y2, xs[2], y, risk_value(row, "hazardDescription"), size=RISK_FONT_SIZE, align="left")
    draw_cell(c, xs[2], y2, xs[3], y, risk_value(row, "risks"), size=RISK_FONT_SIZE, align="left")

    draw_cell(c, xs[3], y2, xs[4], y, risk_value(row, "initialRiskP"), size=RISK_FONT_SIZE)
    draw_cell(c, xs[4], y2, xs[5], y, risk_value(row, "initialRiskS"), size=RISK_FONT_SIZE)
    draw_cell(c, xs[5], y2, xs[6], y, risk_value(row, "initialRiskR"), size=RISK_FONT_SIZE)
    draw_cell(c, xs[6], y2, xs[7], y, risk_value(row, "initialAlarp"), size=RISK_FONT_SIZE)

    draw_cell(c, xs[7], y2, xs[8], y, risk_value(row, "controlMeasures"), size=RISK_FONT_SIZE, bullet=True)

    draw_cell(c, xs[8], y2, xs[9], y, risk_value(row, "residualRiskP"), size=RISK_FONT_SIZE)
    draw_cell(c, xs[9], y2, xs[10], y, risk_value(row, "residualRiskS"), size=RISK_FONT_SIZE)
    draw_cell(c, xs[10], y2, xs[11], y, risk_value(row, "residualRiskR"), size=RISK_FONT_SIZE)
    draw_cell(c, xs[11], y2, xs[12], y, risk_value(row, "residualAlarp"), size=RISK_FONT_SIZE)
    draw_cell(c, xs[12], y2, xs[13], y, risk_value(row, "responsiblePerson"), size=RISK_FONT_SIZE, align="left")

    return y2


def draw_signature_image(c, image_path, x1, y1, x2, y2):
    file_path = resolve_file_path(image_path)
    if not file_path:
        return

    img = ImageReader(file_path)
    img_w, img_h = img.getSize()

    scale = min((x2 - x1) / img_w, (y2 - y1) / img_h)
    final_w = img_w * scale
    final_h = img_h * scale

    x = x1 + 2
    y = y1 + ((y2 - y1) - final_h) / 2
    c.drawImage(img, x, y, final_w, final_h, preserveAspectRatio=True, mask="auto")


def draw_signature_table(c, y, data):
    report = data.get("reportInfo") or data.get("report") or {}

    top = y
    bottom = y - SIGNATURE_HEIGHT
    mid = CONTENT_LEFT + ((CONTENT_RIGHT - CONTENT_LEFT) / 2)

    draw_cell(c, CONTENT_LEFT, bottom, mid, top)
    draw_cell(c, mid, bottom, CONTENT_RIGHT, top)

    # Full width border above date row
    date_border_y = bottom + 16
    c.setLineWidth(0.3)
    c.line(CONTENT_LEFT, date_border_y, CONTENT_RIGHT, date_border_y)

    c.setFont(FONT_BOLD, RISK_FONT_SIZE)
    c.drawString(CONTENT_LEFT + 5, top - 13, "Prepared By:")
    c.drawString(mid + 5, top - 13, "Approved By:")

    # Signature position
    draw_signature_image(
        c,
        report.get("ohsPreparedBySignPath"),
        CONTENT_LEFT + 5,
        top - 38,
        CONTENT_LEFT + 115,
        top - 18
    )

    draw_signature_image(
        c,
        report.get("ohsApprovedBySignPath"),
        mid + 5,
        top - 38,
        mid + 115,
        top - 18
    )

    prepared_by = report_or_default(report, "ohsPreparedBy", report.get("preparedBy"))
    approved_by = report_or_default(report, "ohsApprovedBy", report.get("approvedBy"))
    prepared_date = format_date(report_or_default(report, "ohsPreparedDate", report.get("documentDate")))
    approved_date = format_date(report_or_default(report, "ohsApprovedDate", report.get("documentDate")))

    c.setFont(FONT_BOLD, RISK_FONT_SIZE)

    # Name position (more gap from signature)
    c.drawString(CONTENT_LEFT + 5, bottom + 24, prepared_by)
    c.drawString(mid + 5, bottom + 24, approved_by)

    c.drawString(CONTENT_LEFT + 5, bottom + 6, f"Date: {prepared_date}")
    c.drawString(mid + 5, bottom + 6, f"Date: {approved_date}")

    return bottom


def make_ohs_risk_assessment_pages(data):
    packet = BytesIO()
    c = canvas.Canvas(packet, pagesize=(PAGE_WIDTH, PAGE_HEIGHT))

    start_ohs_page(c)
    y = draw_ohs_summary_table(c, data) - 2
    y, xs = draw_risk_header(c, y)

    rows = risk_rows(data)

    line_height = RISK_FONT_SIZE + 2

    for row in rows:
        blocks = build_risk_line_blocks(row, xs)

        while has_remaining_blocks(blocks):
            available_height = y - PAGE_SAFE_BOTTOM

            if available_height < 30:
                c.showPage()
                start_ohs_page(c)
                y, xs = draw_risk_header(c, 492)
                blocks = build_risk_line_blocks(row, xs) if not has_remaining_blocks(blocks) else blocks
                available_height = y - PAGE_SAFE_BOTTOM

            max_lines = max(1, int((available_height - 10) / line_height))
            total_remaining = remaining_line_count(blocks)
            lines_to_draw = min(max_lines, total_remaining)

            current_blocks, blocks = take_risk_line_blocks(blocks, lines_to_draw)

            row_height = max(30, (lines_to_draw * line_height) + 10)
            y = draw_split_risk_row(c, y, row_height, xs, current_blocks)

            if has_remaining_blocks(blocks):
                c.showPage()
                start_ohs_page(c)
                y, xs = draw_risk_header(c, 492)

    if y - SIGNATURE_HEIGHT < PAGE_SAFE_BOTTOM:
        c.showPage()
        start_ohs_page(c)
        y, xs = draw_risk_header(c, 492)

    draw_signature_table(c, y, data)

    c.save()
    packet.seek(0)

    reader = PdfReader(packet)
    return list(reader.pages)


def make_ohs_risk_assessment_page(data):
    pages = make_ohs_risk_assessment_pages(data)
    return pages[0] if pages else None