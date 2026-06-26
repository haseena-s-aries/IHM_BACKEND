using iText.Commons.Actions;
using iText.Kernel.Colors;
using iText.Kernel.Pdf.Canvas;

using iText.Kernel.Events;
using iText.Kernel.Pdf;
using iText.Kernel.Geom;

namespace Surevey_Certification_App.Controller
{


    public class PageBorderHandler : iText.Kernel.Events.IEventHandler
    {
        public void HandleEvent(Event currentEvent)
        {
            PdfDocumentEvent docEvent = (PdfDocumentEvent)currentEvent;
            PdfPage page = docEvent.GetPage();
            Rectangle pageSize = page.GetPageSize();
            PdfCanvas canvas = new PdfCanvas(page.NewContentStreamBefore(), page.GetResources(), docEvent.GetDocument());

            float margin =18f;

            canvas.SetLineWidth(1);
            canvas.SetStrokeColor(ColorConstants.BLACK);
            canvas.Rectangle(
                margin,
                margin,
                pageSize.GetWidth() - 2 * margin,
                pageSize.GetHeight() - 2 * margin
            );
            canvas.Stroke();
        }
    }

}