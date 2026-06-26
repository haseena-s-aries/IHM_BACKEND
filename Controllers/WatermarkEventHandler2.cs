using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Events;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;

public sealed class WatermarkEventHandler2 : IEventHandler
{
    private readonly PdfFont _font;
    private readonly float _fontSize;
    private readonly string _text;
    private readonly Color _color;
    private readonly ISet<int> _skipPages;

    public WatermarkEventHandler2(
        ISet<int> skipPages,
        string text = "AM/DS-TC-02-R0",
        float fontSize = 12f,
        Color color = null)
    {
        _skipPages = skipPages ?? new HashSet<int>();
        _text = text;
        _fontSize = fontSize;
        _color = color ?? ColorConstants.GRAY;
        _font = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);
    }

    public void HandleEvent(Event currentEvent)
    {
        if (currentEvent is not PdfDocumentEvent docEvent)
            return;

        PdfDocument pdf = docEvent.GetDocument();
        PdfPage page = docEvent.GetPage();

        if (pdf == null || page == null)
            return;

        int pageNumber = pdf.GetPageNumber(page);

        if (_skipPages.Contains(pageNumber))
            return;

        Rectangle pageSize = page.GetPageSize();
        if (pageSize == null)
            return;

        float centerX = pageSize.GetWidth() / 2;
        float y = pageSize.GetBottom() + 5;

        var resources = page.GetResources();
        if (resources == null)
        {
            resources = new PdfResources();
            page.SetResources(resources);
        }

        PdfCanvas canvas = new PdfCanvas(page.NewContentStreamAfter(), resources, pdf);

        canvas.SaveState();
        canvas.BeginText()
              .SetFontAndSize(_font, _fontSize)
              .SetColor(_color ?? ColorConstants.GRAY, true)
              .MoveText(centerX - 400, y)
              .ShowText(_text ?? "")
              .EndText();
        canvas.RestoreState();
    }
}
