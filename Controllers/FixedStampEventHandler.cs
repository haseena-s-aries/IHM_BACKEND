//using System;
//using System.Collections.Generic;
//using iText.IO.Image;
//using iText.Kernel.Events;
//using iText.Kernel.Geom;
//using iText.Kernel.Pdf;
//using iText.Layout;
//using iText.Layout.Element;
//using iText.Kernel.Pdf.Canvas;

//public class FixedStampEventHandler : IEventHandler
//{
//    private readonly string _imagePath;
//    private readonly HashSet<int> _targetPages;

//    public FixedStampEventHandler(string imagePath, int[] targetPages)
//    {
//        _imagePath = imagePath;
//        _targetPages = new HashSet<int>(targetPages);
//    }

//    public void HandleEvent(Event @event)
//    {
//        var docEvent = (PdfDocumentEvent)@event;
//        var pdfDoc = docEvent.GetDocument();
//        var page = docEvent.GetPage();
//        int pageNumber = pdfDoc.GetPageNumber(page);

//        if (!_targetPages.Contains(pageNumber)) return;

//        float x = 36;
//        float y = 36;
//        float width = 80;
//        float height = 60;

//        if (System.IO.File.Exists(_imagePath))
//        {
//            Image image = new Image(ImageDataFactory.Create(_imagePath))
//                .ScaleToFit(width, height)
//                .SetFixedPosition(pageNumber, x, y);

//            new Document(pdfDoc).Add(image); // Safe here — not closing doc
//        }
//        else
//        {
//            PdfCanvas canvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdfDoc);
//            canvas.Rectangle(x, y, width, height);
//            canvas.SetLineWidth(0.5f);
//            canvas.Stroke();
//        }
//    }
//}
