from report_pdf.appendix_two_attachments import make_appendix_two_attachment_pages


def make_appendix_seven_certificate_pages(page_width, page_height, data):
    appendix_seven_data = dict(data)
    appendix_seven_data["appendixTwoAttachments"] = data.get("appendixSevenAttachments") or []

    return make_appendix_two_attachment_pages(
        page_width,
        page_height,
        appendix_seven_data
    )