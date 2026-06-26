using iText.IO.Font.Constants;
using iText.Kernel.Events;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;

public class CustomFooterHandler : IEventHandler
{
    private readonly HashSet<int> _targetPages;

    public CustomFooterHandler(IEnumerable<int> targetPages)
    {
        _targetPages = new HashSet<int>(targetPages);
    }

    public void HandleEvent(Event currentEvent)
    {
        PdfDocumentEvent docEvent = (PdfDocumentEvent)currentEvent;
        PdfDocument pdf = docEvent.GetDocument();
        PdfPage page = docEvent.GetPage();
        int pageNumber = pdf.GetPageNumber(page);

        if (_targetPages.Contains(pageNumber))
        {
            PdfCanvas canvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdf);
            Rectangle pageSize = page.GetPageSize();

            Canvas modelCanvas = new Canvas(canvas, pageSize).SetFontSize(12);

            // Center-aligned footer note
            modelCanvas.ShowTextAligned(
                new Paragraph("(Note: White copy – Aries Administration; Rose copy – Aries Accounts; Pink copy – Client; Yellow copy –\n Book Copy if Carbon book used, not applicable for computer printed copy)").SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                    .SetTextAlignment(TextAlignment.CENTER),
                pageSize.GetWidth() / 2, 75, TextAlignment.CENTER);

            // Left footer
            modelCanvas.ShowTextAligned(
                new Paragraph("Ref: QHSE/P 11/ CL 02/ Rev 07 / 01 Aug 2020").SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN)),
                70, 35, TextAlignment.LEFT);

            // Right footer (Page x of y)
            modelCanvas.ShowTextAligned(
                new Paragraph($"Page 1 of 1").SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN)),
                pageSize.GetWidth() - 50, 25, TextAlignment.RIGHT);

            modelCanvas.Close();
        }
    }
}