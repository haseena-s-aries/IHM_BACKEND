using iText.Kernel.Events;
using iText.Kernel.Geom;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Layout;
using iText.Layout.Renderer;
public sealed class BottomTableEventHandler : IEventHandler
{
    private readonly Document _document;
    private readonly Table _table;
    private readonly HashSet<int> _targetPages;

    private readonly float _leftMargin;
    private readonly float _rightMargin;
    private readonly float _bottomOffset;

    // Main constructor
    public BottomTableEventHandler(
        Document document,
        Table table,
        int[] targetPages,
        float leftMargin,
        float rightMargin,
        float bottomOffset)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _targetPages = new HashSet<int>(targetPages ?? Array.Empty<int>());
        _leftMargin = leftMargin;
        _rightMargin = rightMargin;
        _bottomOffset = bottomOffset;
    }

    // Overload: single page
    public BottomTableEventHandler(
        Document document,
        Table table,
        int targetPage,
        float leftMargin,
        float rightMargin,
        float bottomOffset)
        : this(document, table, new[] { targetPage }, leftMargin, rightMargin, bottomOffset) { }

    // Overload: use document margins + default offset, multiple pages
    public BottomTableEventHandler(Document document, Table table, int[] targetPages, float bottomOffset = 40f)
        : this(document, table, targetPages, document.GetLeftMargin(), document.GetRightMargin(), bottomOffset) { }

    // Overload: single page + defaults
    public BottomTableEventHandler(Document document, Table table, int targetPage, float bottomOffset = 40f)
        : this(document, table, new[] { targetPage }, document.GetLeftMargin(), document.GetRightMargin(), bottomOffset) { }

    public void HandleEvent(Event @event)
    {
        var docEvent = (PdfDocumentEvent)@event;
        var pdfDoc = docEvent.GetDocument();
        var page = docEvent.GetPage();
        int pageNumber = pdfDoc.GetPageNumber(page);

        if (!_targetPages.Contains(pageNumber))
            return;

        var pageSize = page.GetPageSize();
        float availableWidth = pageSize.GetWidth() - _leftMargin - _rightMargin;

        // Measure
        Rectangle measureRect = new Rectangle(_leftMargin, 0, availableWidth, pageSize.GetHeight());
        var measureRenderer = _table.CreateRendererSubTree().SetParent(_document.GetRenderer());
        var measure = measureRenderer.Layout(new LayoutContext(new LayoutArea(pageNumber, measureRect)));
        float tableHeight = measure.GetOccupiedArea().GetBBox().GetHeight();

        // Draw
        Rectangle drawRect = new Rectangle(_leftMargin, _bottomOffset, availableWidth, tableHeight);
        var drawRenderer = _table.CreateRendererSubTree().SetParent(_document.GetRenderer());
        drawRenderer.Layout(new LayoutContext(new LayoutArea(pageNumber, drawRect)));

        var pdfCanvas = new iText.Kernel.Pdf.Canvas.PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdfDoc);
        var drawCtx = new DrawContext(pdfDoc, pdfCanvas, false);
        drawRenderer.Draw(drawCtx);
    }
}
