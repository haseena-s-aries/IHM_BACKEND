import json

import sys

from collections import OrderedDict

from openpyxl import load_workbook
 
 
def clean(value):

    if value is None:

        return ""

    return str(value).replace("\xa0", " ").strip()
 
 
def is_empty_row(values):

    return all(clean(v) == "" for v in values)

EXPECTED_HEADERS = {
    2: "sample no",
    3: "table",
    4: "hazardous materials",
    5: "location",
    6: "name of equipment",
    7: "component",
    8: "quantity of hazmat",
    11: "manufacturer",
    12: "result of document analysis",
    13: "procedure of check",
    15: "result of check",
    16: "remarks",
}

EXPECTED_SUB_HEADERS = {
    8: "unit",
    9: "no",
    10: "total",
    13: "sampling check",
    14: "visual check",
}


def validate_template(ws, header_row):
    missing = []

    for col, expected in EXPECTED_HEADERS.items():
        actual = clean(ws.cell(row=header_row, column=col).value).lower()
        if expected not in actual:
            missing.append(expected)

    sub_header_row = header_row + 1
    for col, expected in EXPECTED_SUB_HEADERS.items():
        actual = clean(ws.cell(row=sub_header_row, column=col).value).lower()
        if expected not in actual:
            missing.append(expected)

    if missing:
        raise ValueError(
            "Invalid Excel format. Please upload the VSCP Checklist Template file. Missing/wrong headers: "
            + ", ".join(missing)
        )
 
def find_header_row(ws):

    for row in range(1, ws.max_row + 1):

        for col in range(1, ws.max_column + 1):

            value = clean(ws.cell(row=row, column=col).value).lower()

            if "sample no" in value:

                return row

    return 3
 
 
def parse_sheet(ws):

    header_row = find_header_row(ws)
    validate_template(ws, header_row)
 
    # Your template has main headers on row 3 and subheaders on row 4.

    data_start_row = header_row + 2
 
    areas = OrderedDict()
 
    for row in range(data_start_row, ws.max_row + 1):

        values = [ws.cell(row=row, column=col).value for col in range(2, 17)]
 
        if is_empty_row(values):

            continue
 
        sample_no = clean(ws.cell(row=row, column=2).value)

        table_type = clean(ws.cell(row=row, column=3).value)

        hazardous_material = clean(ws.cell(row=row, column=4).value)

        location = clean(ws.cell(row=row, column=5).value)

        equipment_name = clean(ws.cell(row=row, column=6).value)

        component = clean(ws.cell(row=row, column=7).value)
 
        quantity_unit_kg = clean(ws.cell(row=row, column=8).value)

        quantity_no = clean(ws.cell(row=row, column=9).value)

        quantity_total_kg = clean(ws.cell(row=row, column=10).value)
 
        manufacturer = clean(ws.cell(row=row, column=11).value)

        document_analysis = clean(ws.cell(row=row, column=12).value)

        sampling_check = clean(ws.cell(row=row, column=13).value)

        visual_check = clean(ws.cell(row=row, column=14).value)

        check_result = clean(ws.cell(row=row, column=15).value)

        remarks = clean(ws.cell(row=row, column=16).value)
 
        if not sample_no and not hazardous_material and not location and not equipment_name:

            continue
 
        area_name = location or "Unspecified Area"
 
        if area_name not in areas:

            areas[area_name] = {

                "areaName": area_name,

                "description": "",

                "sortOrder": len(areas) + 1,

                "checkpoints": []

            }
 
        description_parts = [x for x in [equipment_name, component] if x]

        description = " - ".join(description_parts)
 
        areas[area_name]["checkpoints"].append({

            "checkpointCode": sample_no,

            "description": description,

            "tableType": table_type,

            "location": location,

            "equipmentName": equipment_name,

            "component": component,

            "materialCategory": table_type,

            "hazardousMaterial": hazardous_material,

            "quantityUnitKg": quantity_unit_kg,

            "quantityNo": quantity_no,

            "quantityTotalKg": quantity_total_kg,

            "manufacturerBrandName": manufacturer,

            "documentAnalysisResult": document_analysis,

            "samplingCheck": sampling_check,

            "visualCheck": visual_check,

            "checkResult": check_result,

            "remarks": remarks,

            "sourceRowNumber": row

        })
 
    return list(areas.values())
 
 
def main():

    if len(sys.argv) < 2:

        print(json.dumps({

            "success": False,

            "message": "Excel path is required.",

            "areas": []

        }))

        return
 
    excel_path = sys.argv[1]
 
    try:

        wb = load_workbook(excel_path, data_only=True)

        all_areas = []
 
        for ws in wb.worksheets:
            sheet_areas = parse_sheet(ws)
            all_areas.extend(sheet_areas)

        if not all_areas:
            raise ValueError("Invalid Excel format. No valid VSCP checklist rows found.")
 
        print(json.dumps({

            "success": True,

            "message": "IHM Excel parsed successfully.",

            "areas": all_areas

        }))
 
    except Exception as ex:

        print(json.dumps({

            "success": False,

            "message": str(ex),

            "areas": []

        }))
 
 
if __name__ == "__main__":

    main()

 