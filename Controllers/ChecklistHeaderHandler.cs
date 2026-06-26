using iText.Kernel.Events;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Layout;
using iText.Kernel.Font;

public class ChecklistHeaderHandler : IEventHandler
{
    private readonly string _projectTitle, _jobNumber, _certificateNumber;
    private readonly PdfFont _boldFont, _normalFont;
    private readonly int[] _pagesToApply;

    public ChecklistHeaderHandler(string projectTitle, string jobNumber, string certificateNumber,
                                  PdfFont boldFont, PdfFont normalFont, int[] pagesToApply)
    {
        _projectTitle = projectTitle;
        _jobNumber = jobNumber;
        _certificateNumber = certificateNumber;
        _boldFont = boldFont;
        _normalFont = normalFont;
        _pagesToApply = pagesToApply;
    }

    public void HandleEvent(Event currentEvent)
    {
        var docEvent = (PdfDocumentEvent)currentEvent;
        int pageNum = docEvent.GetDocument().GetPageNumber(docEvent.GetPage());

        // Only apply on checklist pages
        if (!_pagesToApply.Contains(pageNum))
            return;

        var canvas = new Canvas(docEvent.GetPage(), docEvent.GetPage().GetPageSize());
        canvas.SetFont(_normalFont).SetFontSize(10);

        // Aries header
        Table header = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 })).UseAllAvailableWidth();
        header.AddCell(new Cell().Add(new Paragraph("ARIES MARINE SITE CHECKLIST").SetFont(_boldFont)).SetBorder(Border.NO_BORDER));
        header.AddCell(new Cell().Add(new Paragraph("TOWAGE SURVEY").SetFont(_boldFont).SetTextAlignment(TextAlignment.RIGHT)).SetBorder(Border.NO_BORDER));

        // Project info
        Table project = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 })).UseAllAvailableWidth();
        project.AddCell(new Cell().Add(new Paragraph($"PROJECT NAME: {_projectTitle}").SetFont(_normalFont)).SetBorder(Border.NO_BORDER));
        project.AddCell(new Cell().Add(new Paragraph($"CoA No: {_jobNumber} / {_certificateNumber}").SetFont(_normalFont)).SetBorder(Border.NO_BORDER));
        project.AddCell(new Cell().Add(new Paragraph("Route: STEVIN ROCK JETTY, RAS AL KHAIMAH, UAE to BAHRAIN").SetFont(_normalFont)).SetBorder(Border.NO_BORDER));
        project.AddCell(new Cell().Add(new Paragraph($"Job No: {_jobNumber}").SetFont(_normalFont)).SetBorder(Border.NO_BORDER));

        canvas.ShowTextAligned("", 0, 0, TextAlignment.LEFT); // Ensure alignment is set
        canvas.Add(header);
        canvas.Add(new Paragraph("\n"));
        canvas.Add(project);
        canvas.Close();
    }
}
