using iText.IO.Image;
using iText.Kernel.Events;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using iText.Layout.Element;

public class HeaderEventHandler : IEventHandler
{
    private readonly string _imagePath;
    private readonly int _targetPage;

    public HeaderEventHandler(string imagePath, int targetPage)
    {
        _imagePath = imagePath;
        _targetPage = targetPage;
    }

    public void HandleEvent(Event @event)
    {
        var docEvent = (PdfDocumentEvent)@event;
        var pdf = docEvent.GetDocument();
        var page = docEvent.GetPage();
        var currentPage = pdf.GetPageNumber(page);

        if (currentPage != _targetPage) return;

        if (File.Exists(_imagePath))
        {
            var pageSize = page.GetPageSize();
            var pdfCanvas = new PdfCanvas(page.NewContentStreamBefore(), page.GetResources(), pdf);
            var canvas = new Canvas(pdfCanvas, pageSize);

            var headerImage = new Image(ImageDataFactory.Create(_imagePath));

            // 🔥 Adjust image to match page width and stay inside margins
            headerImage.ScaleToFit(pageSize.GetWidth(), pageSize.GetHeight());
            headerImage.SetFixedPosition(currentPage, 0, pageSize.GetTop() - headerImage.GetImageScaledHeight());

            canvas.Add(headerImage);
            canvas.Close();
        }
    }
}