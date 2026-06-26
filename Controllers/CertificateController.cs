using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Events;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Layout;
using iText.Layout.Properties;
using iText.Layout.Splitting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc;
using Org.BouncyCastle.Utilities.Zlib;
using Surevey_Certification_App.Controller;
using Survey_Certification_App.Data;
using Survey_Certification_App.Models;
using System.IO;
using System.Threading.Tasks;
using Path = System.IO.Path;



namespace Survey_Certification_App.Controllers
{
    [AllowAnonymous]
    [Route("api/[controller]")]
    [ApiController]

    public class CertificateController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public CertificateController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpPost("upload-stamp")]
        public async Task<IActionResult> UploadStamp(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var uploadsFolder = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "stamps");

            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var filePath = System.IO.Path.Combine(uploadsFolder, file.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return Ok(new { filePath = $"/stamps/{file.FileName}" });
        }

        [HttpPost("upload-signature")]
        public async Task<IActionResult> UploadSignature(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var uploadsFolder = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "signatures");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var filePath = System.IO.Path.Combine(uploadsFolder, file.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return Ok(new { filePath = $"/signatures/{file.FileName}" });
        }


        [HttpPost("upload-signature-Client")]
        public async Task<IActionResult> UploadSignatureClient(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var uploadsFolder = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "signatures");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var filePath = System.IO.Path.Combine(uploadsFolder, file.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return Ok(new { filePath = $"/signatures/{file.FileName}" });
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateCertificate([FromBody] Certificate model)

        {




            {
                // 🔐 Failsafe fallback to prevent null or binding issues
                foreach (var cargo in model.CargoDetails ?? new List<CargoDetails>())
                {
                    cargo.Remarks = string.IsNullOrWhiteSpace(cargo.Remarks) ? "" : cargo.Remarks;
                    cargo.Location = string.IsNullOrWhiteSpace(cargo.Location) ? "" : cargo.Location;
                    cargo.Cargos = string.IsNullOrWhiteSpace(cargo.Cargos) ? "" : cargo.Cargos;
                    cargo.IDMarkings = string.IsNullOrWhiteSpace(cargo.IDMarkings) ? "" : cargo.IDMarkings;
                    cargo.Weight = string.IsNullOrWhiteSpace(cargo.Weight) ? "" : cargo.Weight;
                    cargo.Dimensions = string.IsNullOrWhiteSpace(cargo.Dimensions) ? "" : cargo.Dimensions;
                }


                if (model == null)
                    return BadRequest("Invalid data");

                // ✅ Hide IssuedDate if NA checkbox is checked
                if (model.NoDateCheckbox)
                {
                    model.IssuedDate = null;
                }

                try
                {
                    using (var outputStream = new MemoryStream())
                    {
                        using (var writer = new PdfWriter(outputStream))
                        {
                            using (var pdf = new iText.Kernel.Pdf.PdfDocument(writer))
                            {

                                // BEFORE you start writing the document:
                                var skipPages = new HashSet<int>();
                                var wmHandler = new WatermarkEventHandler2(skipPages);
                                pdf.AddEventHandler(PdfDocumentEvent.END_PAGE, wmHandler);

                                //// ✅ Manually cadd a page first
                                //pdf.AddEventHandler(PdfDocumentEvent.END_PAGE, new WatermarkEventHandler2());


                                var document = new Document(pdf, iText.Kernel.Geom.PageSize.A3); // OR PageSize.A4
                                document.SetMargins(145, 50, 100, 50);


                                pdf.AddEventHandler(PdfDocumentEvent.END_PAGE, new PageBorderHandler());




                                // ✅ Load dynamic header image from model.HeaderImagePath
                                string imagePath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model.HeaderImagePath.TrimStart('/'));
                                if (System.IO.File.Exists(imagePath))
                                {
                                    var pageSize = pdf.GetDefaultPageSize();
                                    float pageWidth = pageSize.GetWidth();
                                    float imageHeight = 150;
                                    float adjustedWidth = pageWidth * 0.95f; // Reduce width to 98% (tweak this)

                                    Image headerImage = new Image(ImageDataFactory.Create(imagePath))
                                        .ScaleToFit(adjustedWidth, imageHeight)
                                        .SetFixedPosition((pageWidth - adjustedWidth) / 2, pageSize.GetHeight() - imageHeight) // Center horizontally

                                        .SetMarginLeft(0).SetMarginRight(0); // Not required, but keeps things clean

                                    document.Add(headerImage);
                                }
                                else
                                {
                                    // Optional: Add empty space or skip entirely
                                    document.Add(new Paragraph(" ").SetHeight(20f)); // or simply skip this block
                                }


                                var jobPara = new Paragraph($"{model.JobNumber}/{model.CertificateNumber}")
           .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
           .SetFontSize(18)
           .SetTextAlignment(TextAlignment.RIGHT);

                                var jobDiv = new Div()
                                    .SetWidth(UnitValue.CreatePercentValue(90)) // only 90% width, so moves left
                                    .Add(jobPara);

                                document.Add(jobDiv);



                                // ✅ Add Certificate Title
                                document.Add(new Paragraph("CERTIFICATE OF APPROVAL - TOWAGE")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                    .SetFontSize(20)
                                    .SetTextAlignment(TextAlignment.CENTER));

                                // 🧠 Temporarily reduce top margin (only affects following content unless overflow occurs)
                                float originalTop = document.GetTopMargin();
                                float originalRight = document.GetRightMargin();
                                float originalBottom = document.GetBottomMargin();
                                float originalLeft = document.GetLeftMargin();

                                // Reduce top margin for the annexure pages only
                                document.SetMargins(20, originalRight, originalBottom, originalLeft);

                                   float cmToPt = 28.35f;
                                float innerMargin = 2.0f * cmToPt;              // ~2 cm each side (adjust if you like)
                                document.SetMargins(innerMargin, innerMargin, innerMargin, innerMargin);


                                // Define the font
                                var boldFont = PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD);

                                // Create a table with 2 columns (left fixed width)
                                Table table = new Table(UnitValue.CreatePercentArray(new float[] { 30, 70 })) // 30% left, 70% right
                                    .UseAllAvailableWidth()
                                    .SetFixedLayout()
                                     .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin);

                                // Set border style for each cell for visual clarity
                                iText.Layout.Style borderStyle = new iText.Layout.Style()
                                    .SetBorder(new SolidBorder(ColorConstants.BLACK, 1));

                                // Add cells with fixed-width labels and consistent formatting

                                // PROJECT
                                table.AddCell(new Cell().Add(new Paragraph("PROJECT"))
                                    .AddStyle(borderStyle)
                                    .SetPadding(1)   // inside table cells
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetFontSize(14)
                                     .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin)


                                    .SetFont(boldFont));

                                table.AddCell(new Cell().Add(new Paragraph(model.ProjectTitle))
                                    .AddStyle(borderStyle)
                                    .SetPadding(1)   // inside table cells
                                    .SetFontSize(14)
                                        .SetTextAlignment(TextAlignment.JUSTIFIED)
                                    .SetFont(boldFont));

                                // CLIENT
                                table.AddCell(new Cell().Add(new Paragraph("CLIENT"))
                                    .AddStyle(borderStyle)
                                    .SetPadding(1)   // inside table cells

                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetFontSize(14)
                                    .SetFont(boldFont));

                                table.AddCell(new Cell().Add(new Paragraph(model.ClientName))
                                    .AddStyle(borderStyle)
                                    .SetPadding(1)   // inside table cells
                                    .SetFontSize(14)
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                    .SetFont(boldFont));

                                // NAME OF TOW
                                table.AddCell(new Cell().Add(new Paragraph("NAME OF TOW"))
                                    .AddStyle(borderStyle)
                                    .SetPadding(1)   // inside table cells
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetFontSize(14)
                                    .SetFont(boldFont));

                                table.AddCell(new Cell().Add(new Paragraph(model.TowName))
                                    .AddStyle(borderStyle)
                                    .SetFontSize(14)
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                    .SetFont(boldFont));

                                // FROM LOCATION
                                table.AddCell(new Cell().Add(new Paragraph("FROM LOCATION"))
                                    .AddStyle(borderStyle)
                                    .SetPadding(1)   // inside table cells
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)

                                    .SetFontSize(14)
                                    .SetFont(boldFont));

                                table.AddCell(new Cell().Add(new Paragraph(model.DeparturePort))
                                    .AddStyle(borderStyle)
                                    .SetPadding(1)   // inside table cells
                                    .SetFontSize(14)
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                    .SetFont(boldFont));

                                // TO LOCATION
                                table.AddCell(new Cell().Add(new Paragraph("TO LOCATION"))
                                    .AddStyle(borderStyle)
                                    .SetPadding(1)   // inside table cells
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetFontSize(14)
                                    .SetFont(boldFont));

                                table.AddCell(new Cell().Add(new Paragraph(model.ArrivalPort))
                                    .AddStyle(borderStyle)
                                    .SetPadding(1)   // inside table cells
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                    .SetFontSize(14)
                                    .SetFont(boldFont));

                                // NAME OF TUG
                                table.AddCell(new Cell().Add(new Paragraph("NAME OF TUG"))
                                    .AddStyle(borderStyle)
                                    .SetPadding(1)   // inside table cells
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetFontSize(14)
                                    .SetFont(boldFont));

                                table.AddCell(new Cell().Add(new Paragraph(model.TugName))
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                    .AddStyle(borderStyle)
                                    .SetPadding(1)   // inside table cells
                                    .SetFontSize(14)
                                    .SetFont(boldFont));

                                // CARGO
                                table.AddCell(new Cell().Add(new Paragraph("CARGO"))
                                    .AddStyle(borderStyle)
                                    .SetPadding(1)   // inside table cells
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetFontSize(14)
                                    .SetFont(boldFont));

                                table.AddCell(new Cell().Add(new Paragraph(model.Cargo))
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                    .AddStyle(borderStyle)
                                    .SetPadding(1)   // inside table cells
                                    .SetFontSize(14)
                                    .SetFont(boldFont));

                                // Add the table to the document
                                document.Add(table);

                                document.Add(new Paragraph("\n"));

                                PdfFont timesRoman = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);

                                document.Add(new Paragraph($"This is to certify that this office represented by the undersigned, acting on request of {model.ClientName}, has inspected the above combination and confirms that it has been prepared for the above towage.")
                                     .SetFont(timesRoman)
                                    .SetFontSize(16)
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                     .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));
                                document.Add(new Paragraph("\n"));

                                document.Add(new Paragraph($"The tug, tow and towing arrangements for the proposed voyage are approved subject to satisfactory compliance with the attached recommendations (Annexure-1).")
                                         .SetFont(timesRoman)
                                    .SetFontSize(16)
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                     .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));
                                document.Add(new Paragraph("\n"));

                                document.Add(new Paragraph($"Departure from {model.DeparturePort} shall take place only with the specific agreement of the undersigned or his nominated deputy, following submission of the favorable 48 hrs weather forecast and close-out of the annexed recommendations.")
                                        .SetFont(timesRoman)
                                    .SetFontSize(16)
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                     .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));
                                document.Add(new Paragraph("\n"));

                                // ✅ CONDITIONS SECTION
                                document.Add(new Paragraph($"This Certificate of Approval is issued subject to {model.CertificateType} Terms and Conditions of Business, current at the time of issue. without prejudice to any insurance interests or to any or all parties concerned. Any alterations in the surveyed items or deviations from the agreed procedures after issue of this Certificate may render the Certificate invalid unless specifically approved by this office.")
                                        .SetFont(timesRoman)
                                    .SetFontSize(16)
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                     .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));
                                document.Add(new Paragraph("\n"));

                                // -------- Build the Responsibility Table --------
                                PdfFont normalFont = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);

                                // Create a table with 4 columns and ensure it's centered
                                Table table1 = new Table(UnitValue.CreatePercentArray(new float[] { 3, 5, 3, 3 })) // 4 columns
                                    .UseAllAvailableWidth()
                                    .SetTextAlignment(TextAlignment.CENTER) // Center the table
                                    .SetHorizontalAlignment(HorizontalAlignment.CENTER) // Center alignment
                                    .SetFont(normalFont)
                                    .SetFontSize(10)
                                    .SetMarginTop(0)
                                     .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin)
                                    .SetMarginBottom(0);
                                // Header Row
                                string[] headers = { "Responsibility", "Name", "Signature", "Date & Time" };
                                foreach (var header in headers)
                                {
                                    table1.AddHeaderCell(
                                        new Cell()
                                            .Add(new Paragraph(header)
                                                .SetFont(boldFont)
                                                .SetFontSize(11)
                                                .SetTextAlignment(TextAlignment.LEFT))
                                            .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                            .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                            .SetPadding(4)
                                    );
                                }

                                // Row: Responsibility
                                table1.AddCell(new Cell().Add(new Paragraph("Issued By").SetFont(boldFont).SetFontSize(11))
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                    .SetPadding(4)
                                    .SetTextAlignment(TextAlignment.LEFT));

                                // Helper method to handle null or empty values
                                static string Safe(string? s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();

                             
                                table1.AddCell(new Cell()
                                    .Add(new Paragraph(Safe(model.SurveyedBy ?? ""))
                                        .SetFont(normalFont)
                                        .SetFontSize(10)
                                        .SetTextAlignment(TextAlignment.LEFT))
                                    .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                    .SetPadding(4));

                                // Signature Row
                                string signaturePath1 = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model.SignaturePath?.TrimStart('/') ?? "");
                                if (System.IO.File.Exists(signaturePath1))
                                {
                                    var signatureImage = new Image(ImageDataFactory.Create(signaturePath1)).SetWidth(70).SetHeight(35);
                                    table1.AddCell(new Cell().Add(signatureImage).SetPadding(4).SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));
                                }
                                else
                                {
                                    table1.AddCell(new Cell().Add(new Paragraph("")).SetPadding(4).SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));
                                }


                                // Date Row
                                table1.AddCell(new Cell()
                                    .Add(new Paragraph(model.IssuedDate.HasValue ? model.IssuedDate.Value.ToString("dd/MMM/yyyy") : "")
                                        .SetFont(normalFont).SetFontSize(10))
                                    .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                    .SetPadding(4)
                                    .SetTextAlignment(TextAlignment.LEFT));
                                string stampImagePath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model.StampPath?.TrimStart('/') ?? "");
                                if (System.IO.File.Exists(stampImagePath))
                                {
                                    var stampImage = new Image(ImageDataFactory.Create(stampImagePath))
                                        .SetWidth(120)
                                        .SetHeight(100)
                                         .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin)
                                        .SetTextAlignment(TextAlignment.LEFT);

                                    // Ensure the stamp is kept together
                                    document.Add(new Div().Add(stampImage).SetKeepTogether(true));
                                }

                                // Add "For and on behalf of" section
                                document.Add(new Paragraph("For and on behalf of")
                                    .SetFont(timesRoman)
                                    .SetFontSize(14)
                                    .SetMarginBottom(2)
                                    .SetKeepWithNext(true)
                                     .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));  // Ensures this stays on the same page as the next line

                                document.Add(new Paragraph(model.CertificateType ?? "")
                                    .SetFont(boldFont)
                                    .SetFontSize(14)
                                    .SetKeepTogether(true)
                                     .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));  // Keeps the certificate type text together
                                // -------- Dynamically determine bottom placement --------
                                float availableHeight = pdf.GetDefaultPageSize().GetHeight()
                                    - document.GetTopMargin()
                                    - document.GetBottomMargin();

                                // Measure the height of the table
                                Rectangle measureRect = new Rectangle(document.GetLeftMargin(), 0,
                                    pdf.GetDefaultPageSize().GetWidth() - document.GetLeftMargin() - document.GetRightMargin(),
                                    availableHeight);

                                var measureRenderer = table1.CreateRendererSubTree().SetParent(document.GetRenderer());
                                var measure = measureRenderer.Layout(new LayoutContext(new LayoutArea(pdf.GetNumberOfPages(), measureRect)));
                                float tableHeight = measure.GetOccupiedArea().GetBBox().GetHeight();

                                // If not enough space on current page → move to next page
                                var currentPage = pdf.GetLastPage();
                                float yPosition = currentPage.GetPageSize().GetHeight() - document.GetBottomMargin() - tableHeight;
                                if (yPosition < document.GetBottomMargin() + 50)
                                {
                                    pdf.AddNewPage();
                                }

                                // -------- Pin the table on the right side at the bottom --------
                                int footerPage = pdf.GetNumberOfPages();

                                // Shift table slightly to the right (in points)
                                float shiftRight = 20f; // Adjust this value between 10–25 for best alignment

                                pdf.AddEventHandler(
                 PdfDocumentEvent.END_PAGE,
                 new BottomTableEventHandler(
                     document,
                     table1,
                     new[] { footerPage },
                     document.GetLeftMargin(),
                     document.GetRightMargin(),
                     40f
                 )
             );



                                //static float MeasureTableHeight(Document doc, PdfDocument pdfDoc, Table table)
                                //{
                                //    int pageNum = Math.Max(1, pdfDoc.GetNumberOfPages());
                                //    var pageSize1 = pdfDoc.GetDefaultPageSize();
                                //    float width = pageSize1.GetWidth() - doc.GetLeftMargin() - doc.GetRightMargin();

                                //    var measureRect = new Rectangle(doc.GetLeftMargin(), 0, width, pageSize1.GetHeight());
                                //    var measureRenderer = table.CreateRendererSubTree().SetParent(doc.GetRenderer());
                                //    var result = measureRenderer.Layout(new LayoutContext(new LayoutArea(pageNum, measureRect)));
                                //    return result.GetOccupiedArea().GetBBox().GetHeight();
                                //}

                                PdfFont normalFont1 = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);
                                // Assumes you already have: PdfFont boldFont; PdfFont timesRoman;

                                Table table2 = new Table(UnitValue.CreatePercentArray(new float[] { 3, 5, 3, 3 })) // 4 cols
                                    .SetHorizontalAlignment(HorizontalAlignment.CENTER)  // center the table body
                                    .SetTextAlignment(TextAlignment.LEFT)
                                    .SetFont(normalFont).SetFontSize(10)
                                    .SetMarginTop(0).SetMarginBottom(0);

                                // Header Row
                                string[] headers1 = { "Responsibility", "Name", "Signature", "Date & Time" };
                                foreach (var header in headers1)
                                {
                                    table2.AddHeaderCell(
                                        new Cell()
                                            .Add(new Paragraph(header)
                                                .SetFont(boldFont)
                                                .SetFontSize(11)
                                                .SetTextAlignment(TextAlignment.LEFT))
                                            .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                            .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                            .SetPadding(4)
                                    );
                                }

                                // Row: Responsibility
                                table2.AddCell(new Cell().Add(new Paragraph("Issued By").SetFont(boldFont).SetFontSize(11))
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                    .SetPadding(4)
                                    .SetTextAlignment(TextAlignment.LEFT));

                                // Row: Name
                                table2.AddCell(new Cell()
                                    .Add(new Paragraph(Safe(model.SurveyedBy))
                                        .SetFont(normalFont)
                                        .SetFontSize(10)
                                        .SetTextAlignment(TextAlignment.LEFT))
                                    .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                    .SetPadding(4));

                                // Row: Signature
                                {
                                    string signaturePath3 = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model.SignaturePath?.TrimStart('/') ?? "");
                                    if (System.IO.File.Exists(signaturePath3))
                                    {
                                        var signatureImage = new Image(ImageDataFactory.Create(signaturePath1)).SetWidth(70).SetHeight(35);
                                        table1.AddCell(new Cell().Add(signatureImage).SetPadding(4).SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));
                                    }
                                    else
                                    {
                                        table1.AddCell(new Cell().Add(new Paragraph("")).SetPadding(4).SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));
                                    }
                                }

                                // Row: Date
                                table2.AddCell(new Cell()
                                    .Add(new Paragraph(model.IssuedDate.HasValue ? model.IssuedDate.Value.ToString("dd/MMM/yyyy") : "")
                                        .SetFont(normalFont).SetFontSize(10))
                                    .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                    .SetPadding(4)
                                    .SetTextAlignment(TextAlignment.LEFT));
                                // ================== Reserve bottom space so nothing overlaps the footer table ====================
                                //float footerHeight = MeasureTableHeight(document, pdf, table2);
                                //document.SetMargins(
                                //    document.GetTopMargin(),
                                //    document.GetRightMargin(),
                                //    Math.Max(document.GetBottomMargin(), footerHeight + 10f), // reserve just a bit more
                                //    document.GetLeftMargin()
                                //);

                                //==============================================================================================================================
                                // 🧠 Temporarily reduce top margin (only affects following content unless overflow occurs)
                                //float originalTop = document.GetTopMargin();
                                //float originalRight = document.GetRightMargin();
                                //float originalBottom = document.GetBottomMargin();
                                //float originalLeft = document.GetLeftMargin();

                                // Reduce top margin for the annexure pages only
                                document.SetMargins(20, originalRight, originalBottom, originalLeft);

                                // Start ANNEXURE section on a fresh page
                                int recStartPage = pdf.GetNumberOfPages() + 1;
                                document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));

                                // Title + Cert number
                                document.Add(new Paragraph("ANNEXURE-1: RECOMMENDATIONS TO CERTIFICATE OF APPROVAL")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                    .SetFontSize(18)
                                    .SetTextAlignment(TextAlignment.CENTER));

                                document.Add(new Paragraph($"{model.JobNumber}/{model.CertificateNumber}")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                    .SetFontSize(16)
                                    .SetTextAlignment(TextAlignment.CENTER));
                                document.Add(new Paragraph("\n"));

                                // Ensure defaults (trimmed)
                                if (model.defaultRecommendations == null || !model.defaultRecommendations.Any())
                                {
                                    model.defaultRecommendations = new List<DRecommendations>
    {
        new() { RecommendationText = "Voyage to commence within 24 hours from the time of our survey." },
        new() { RecommendationText = "Tow to depart in favorable weather conditions and forecast, typically less than Beaufort Force 5." },
        new() { RecommendationText = "Good seamanship to be followed, including but not limited to the following." },
        new() { RecommendationText = "Frequent weather conditions to be obtained by the tug boat from meteorological authorities en-route." },
        new() { RecommendationText = "Speed of tow to be controlled, commensurate with sea and weather conditions to prevent damage to vessel(s)." },
        new() { RecommendationText = "Visual check to be kept at all times to the towed vessel with particular attention to lashing arrangements, towing gear, navigational lights and shapes and draft marks." },
        new() { RecommendationText = "Tug boat’s log book to be kept updated, showing above supervisions as well as towing speed, weather conditions and forecasts, radio communication and other relevant particulars." },
        new() { RecommendationText = "Vessel’s courses and positions to be plotted at regular intervals on navigational charts and same together with official log book to be available for review with relevant authorities." },
        new() { RecommendationText = "Additional tug assistance to be provided for steering in restricted channels as necessary." },
        new() { RecommendationText = "Pilots to be engaged on arrival in local waters, restricted channels and intermediate ports if any." },
        new() { RecommendationText = "All flush scuttles on both units to be properly closed and sealed at all times during the passage. All water tight doors on both units to be properly closed at all times of passage except when essential access by personnel is required." },
        new() { RecommendationText = "Daylight towing signals to be created and navigations light to be lit during the hours of darkness on both units." },
        new() { RecommendationText = "All parts of towage arrangements are to be free without any obstacles." },
        new() { RecommendationText = "The Master/operator is to provide Aries Oil Field Services L.L.C office with a daily report on the vessel’s course, speed, heading, ROB figures, weather conditions, distance covered and ETA at the destination port. Any deviation for weather routing etc. is to be reported to ariesmarine@ariesgroup.ae" }
    };
                                }
                                else
                                {
                                    foreach (var r in model.defaultRecommendations)
                                        r.RecommendationText = Safe(r.RecommendationText);
                                }

                                // Make the recommendations table a bit narrower & centered
                                var pageSize2 = pdf.GetDefaultPageSize();
                                float contentWidth = pageSize2.GetWidth() - document.GetLeftMargin() - document.GetRightMargin();
                                float recTableWidth = contentWidth * 0.85f; // shrink to 85% of text area

                                Table recommendationTable1 = new Table(UnitValue.CreatePercentArray(new float[] { 1, 7 }))
                                    .SetWidth(UnitValue.CreatePointValue(recTableWidth))
                                    .SetHorizontalAlignment(HorizontalAlignment.CENTER)
                                    .SetFont(timesRoman)
                                    .SetFontSize(16);

                                // Header
                                recommendationTable1.AddHeaderCell(
                                    new Cell()
                                        .Add(new Paragraph("SL. No.").SetBold().SetTextAlignment(TextAlignment.CENTER))
                                        .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                        .SetPadding(6)
                                );
                                recommendationTable1.AddHeaderCell(
                                    new Cell()
                                        .Add(new Paragraph("Recommendations to be completed prior departure").SetBold().SetTextAlignment(TextAlignment.CENTER))
                                        .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                        .SetPadding(6)
                                );

                                // Rows
                                for (int i = 0; i < model.defaultRecommendations.Count; i++)
                                {
                                    recommendationTable1.AddCell(
                                        new Cell().Add(new Paragraph((i + 1).ToString()))
                                            .SetTextAlignment(TextAlignment.CENTER)
                                            .SetPadding(6)
                                    );
                                    recommendationTable1.AddCell(
                                        new Cell().Add(new Paragraph(model.defaultRecommendations[i].RecommendationText))
                                            .SetTextAlignment(TextAlignment.LEFT)
                                            .SetPadding(6)
                                    );
                                }

                                // Add the rec table (iText will flow across pages naturally)
                                document.Add(recommendationTable1);
                                document.Add(new Paragraph("\n"));

                                // Last page that contains recommendations
                                int recEndPage = pdf.GetNumberOfPages();

                                // ================== Pin the RESPONSIBILITY table at the bottom of the last ANNEXURE page =========
                                pdf.AddEventHandler(
                                    PdfDocumentEvent.END_PAGE,
                                    new BottomTableEventHandler(
                                        document,
                                        table1,
                                        recEndPage,        // single-page overload: last page of the annexure
                                        bottomOffset: 40f  // distance from physical bottom
                                    )
                                );

                         

                                //==============================================================================================================================


                                // ✅ ANNEXURE – 2 : Cargo Details Section
                                if (model.IncludeCargoDetails && model.CargoDetails?.Any() == true)
                                {
                                    int cargoStartPage = pdf.GetNumberOfPages() + 1;

                                    // Always start on a fresh page
                                    document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));

                                    // ------------------ HEADER ------------------
                                    document.Add(new Paragraph("ANNEXURE-2 CARGO DETAILS")
                                        .SetFont(boldFont)
                                        .SetFontSize(18)
                                        .SetTextAlignment(TextAlignment.CENTER)
                                        .SetMarginTop(0));

                                    document.Add(new Paragraph($"{model.JobNumber}/{model.CertificateNumber}")
                                        .SetFont(boldFont)
                                        .SetFontSize(16)
                                        .SetTextAlignment(TextAlignment.CENTER)
                                        .SetMarginTop(0));

                                    document.Add(new Paragraph("CARGO DETAILS")
                                        .SetFont(boldFont)
                                        .SetFontSize(12)
                                        .SetTextAlignment(TextAlignment.CENTER)
                                        .SetMarginTop(5));

                                    // ------------------ CARGO DETAILS TABLE ------------------
                                    Table cargoTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 3, 4, 3, 2, 3, 3 }))
                                        .UseAllAvailableWidth()
                                        .SetMarginTop(10);

                                    // ✅ Header Row Added
                                    string[] headers4 = { "Sr.", "Location", "Cargo", "ID markings", "Weight", "Dimensions", "Remarks" };
                                    foreach (var header in headers4)
                                    {
                                        cargoTable.AddHeaderCell(new Cell()
                                            .Add(new Paragraph(header)
                                                .SetFont(boldFont)
                                                .SetFontSize(13)
                                                .SetTextAlignment(TextAlignment.CENTER))
                                            .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                            .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                            .SetPadding(4));
                                    }

                                    // ------------------ Data Rows ------------------
                                    // ------------------ Data Rows (with dynamic Sr. No.) ------------------
                                    int srNo = 1;
                                    foreach (var cd in model.CargoDetails)
                                    {
                                        string[] values = {
        srNo.ToString(),  // 🔹 Auto-generated serial number
        cd.Location ?? "",
        cd.Cargos ?? "",
        cd.IDMarkings ?? "",
        cd.Weight ?? "",
        cd.Dimensions ?? "",
        cd.Remarks ?? ""
    };

                                        srNo++; // 🔹 Increment for next row


                                        for (int i = 0; i < values.Length; i++)
                                        {
                                            Paragraph para = new Paragraph(values[i])
                                                .SetFont(normalFont)
                                                .SetFontSize(10)
                                                .SetMultipliedLeading(1.2f)
                                                .SetTextAlignment(i == 0 ? TextAlignment.CENTER : TextAlignment.LEFT)
                                                .SetWordSpacing(0.5f)
                                                .SetCharacterSpacing(0.1f);

                                            para.SetProperty(Property.SPLIT_CHARACTERS, new CustomSplitCharacters());

                                            var cell = new Cell()
                                                .Add(para)
                                                .SetVerticalAlignment(VerticalAlignment.MIDDLE)
                                                .SetTextAlignment(i == 0 ? TextAlignment.CENTER : TextAlignment.LEFT)
                                                .SetPadding(5)
                                                .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                                .SetKeepTogether(true);

                                            cargoTable.AddCell(cell);
                                        }
                                    }

                                    // Add to document
                                    document.Add(cargoTable);
                                    document.Add(new Paragraph("\n\n"));

                                    // ------------------ RESPONSIBILITY FOOTER TABLE (Location removed) ------------------
                                    Table cargoResponsibilityTable = new Table(UnitValue.CreatePercentArray(new float[] { 2.5f, 4f, 3f, 3f }))
                                        .UseAllAvailableWidth()
                                        .SetFont(normalFont)
                                        .SetFontSize(12);

                                    string[] respHeaders = { "Responsibility", "Name", "Signature", "Date & Time" };
                                    foreach (var header in respHeaders)
                                    {
                                        cargoResponsibilityTable.AddHeaderCell(new Cell()
                                            .Add(new Paragraph(header)
                                                .SetFont(boldFont)
                                                .SetFontSize(13))
                                            .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                            .SetTextAlignment(TextAlignment.LEFT)
                                            .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                            .SetPadding(4));
                                    }

                                    // Issued By Row
                                    cargoResponsibilityTable.AddCell(new Cell()
                                        .Add(new Paragraph("Issued By").SetFont(boldFont).SetFontSize(12))
                                        .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));

                                    cargoResponsibilityTable.AddCell(new Cell()
                                        .Add(new Paragraph(model.SurveyedBy ?? "")
                                        .SetFont(normalFont).SetFontSize(12))
                                        .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));

                                    // Signature Cell
                                    string issuedSignaturePath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model.SignaturePath?.TrimStart('/') ?? "");
                                    if (System.IO.File.Exists(issuedSignaturePath))
                                    {
                                        Image sigImage = new Image(ImageDataFactory.Create(issuedSignaturePath)).SetWidth(80).SetHeight(40);
                                        cargoResponsibilityTable.AddCell(new Cell().Add(sigImage)
                                            .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));
                                    }
                                    else
                                    {
                                        cargoResponsibilityTable.AddCell(new Cell().Add(new Paragraph(""))
                                            .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));
                                    }

                                    // Date Cell
                                    cargoResponsibilityTable.AddCell(new Cell()
                                        .Add(new Paragraph(
                                            model.IssuedDate.HasValue ? model.IssuedDate.Value.ToString("dd/MMM/yyyy") : "")
                                            .SetFont(normalFont)
                                            .SetFontSize(12))
                                        .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));

                                    // ------------------ FIX FOOTER POSITION ------------------
                                    int cargoDetailsPage = pdf.GetNumberOfPages();

                                    float rightShift = 25f; // Shift slightly right
                                    float bottomOffset = 35f; // Distance from bottom

                                    pdf.AddEventHandler(
                                        PdfDocumentEvent.END_PAGE,
                                        new BottomTableEventHandler(
                                            document,
                                            cargoResponsibilityTable,
                                            cargoDetailsPage,
                                            document.GetLeftMargin() + rightShift, // 👈 Move right
                                            document.GetRightMargin() - 10f,       // 👈 Make it slightly wider
                                            bottomOffset
                                        )
                                    );
                                }



                                //==========================================================================================================================================================================================
                                // 📌 Add Page Break for new section
                                int workDonePage = pdf.GetNumberOfPages() + 1;   // page that will be created next
                                document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                                skipPages.Add(workDonePage);

                                // --- Work Done Report page: tighter layout and pulled upward ---
                                float saveTop = document.GetTopMargin();
                                float saveRight = document.GetRightMargin();
                                float saveBot = document.GetBottomMargin();
                                float saveLeft = document.GetLeftMargin();



                                // Reduce page margins slightly to move content upward
                                document.SetMargins(30, saveRight, 30, saveLeft);

                                // Step 2: Load your logo
                                string imagePath1 = System.IO.Path.Combine("wwwroot/images", "Aries.png");
                                Image logo = new Image(ImageDataFactory.Create(imagePath1)).ScaleToFit(100, 80);

                                // Step 3: Header table
                                Table headerTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 4 }))
                                    .UseAllAvailableWidth()
                                    .SetMarginBottom(5); // reduce spacing

                                headerTable.AddCell(new Cell().Add(logo)
                                    .SetBorder(Border.NO_BORDER)
                                    .SetTextAlignment(TextAlignment.LEFT)
                                    .SetVerticalAlignment(VerticalAlignment.MIDDLE));

                                PdfFont boldFont1 = PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD);

                                headerTable.AddCell(new Cell().Add(new Paragraph("Work Done Report")
                                        .SetFont(boldFont)
                                        .SetFontSize(16) // ↓ smaller font
                                        .SetTextAlignment(TextAlignment.RIGHT))
                                    .SetBorder(Border.NO_BORDER)
                                    .SetVerticalAlignment(VerticalAlignment.MIDDLE));

                                document.Add(headerTable);
                                document.Add(new Paragraph("\n\n\n\n\n"));
                                document.Add(new Paragraph("\n")); // small gap// small gap
                                //                                   // --- Uniform inner margins for this page/section ---
                                //float cmToPt = 28.35f;
                                //float innerMargin = 2.0f * cmToPt;              // ~2 cm each side (adjust if you like)
                                document.SetMargins(innerMargin, innerMargin, innerMargin, innerMargin);

                                // --- Fonts ---
                                PdfFont times = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);
                                PdfFont timesBold = PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD);

                                // --- Intro paragraph (top of page) ---
                                document.Add(
                                    new Paragraph(
                                        $"This is certified that {model.CertificateType} carried out TOWAGE APPROVAL SURVEY on project {model.ProjectTitle} at {model.DeparturePort}, as per the requirements of {model.ClientName}.")
                                        .SetFont(times)
                                        .SetFontSize(16)
                                        .SetMultipliedLeading(1.2f)
                                        .SetTextAlignment(TextAlignment.LEFT)   // screenshot shows left align
                                        .SetMarginTop(0)
                                        .SetMarginBottom(14)
                                        .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin)
                                );

                                document.Add(new Paragraph("\n\n\n\n\n\n\n"));


                                //// --- Adjust margins (slightly smaller left-right) ---
                                //float cmToPt = 28.35f;
                                //float innerMargin = 1.5f * cmToPt;   // ≈ 1.5 cm per side
                                //document.SetMargins(innerMargin, innerMargin, innerMargin, innerMargin);

                                //// --- Define fonts ---
                                //PdfFont times = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);
                                //PdfFont timesBold = PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD);

                                // --- Heading: “Job Details:” ---
                                document.Add(
                                    new Paragraph("Job Details:")
                                        .SetFont(timesBold)
                                        .SetFontSize(14)
                                        .SetUnderline()
                                        .SetMarginTop(0)
                                        .SetMarginBottom(8)
                                        .SetTextAlignment(TextAlignment.LEFT)
                                         .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin)

                                );
                               
                                // --- Two-column layout: narrow label, wide value ---
                                Table jobDetailsTable = new Table(UnitValue.CreatePercentArray(new float[] { 25, 75 }))
                                    .UseAllAvailableWidth()
                                    .SetBorder(Border.NO_BORDER);
                                // Row: Job No
                                jobDetailsTable.AddCell(new Cell()
                                    .Add(new Paragraph("Job No:")
                                        .SetFont(times)
                                        .SetFontSize(14))
                                    .SetBorder(Border.NO_BORDER)
                                    .SetPaddingLeft(56)   // 👈 Shift slightly right (5–12 pt range ideal)
                                    .SetPaddingBottom(6));


                                jobDetailsTable.AddCell(new Cell()
                                    .Add(new Paragraph(model.JobNumber ?? string.Empty)
                                        .SetFont(times)
                                        .SetFontSize(14))
                                    .SetBorder(Border.NO_BORDER)
                                     .SetPaddingRight(60)
                                    .SetPaddingBottom(18));
                                document.Add(new Paragraph("\n"));
                                // Row: Duration of Job (single line)
                                Paragraph durationLine = new Paragraph()
                                    .Add("From ")
                                    .Add(new Text("________________________"))
                                    .Add("  To ")
                                    .Add(new Text("________________________"))
                                    .SetFont(times)
                                    .SetFontSize(14)

                                    .SetKeepTogether(true)
                                    .SetFixedLeading(16);

                                jobDetailsTable.AddCell(new Cell()
                                    .Add(new Paragraph("Duration of Job:")
                                        .SetFont(times)
                                        .SetFontSize(14))
                                    .SetBorder(Border.NO_BORDER)
                                    .SetPaddingLeft(56)
                                    .SetPaddingBottom(18));

                                jobDetailsTable.AddCell(new Cell()
                                    .Add(durationLine)
                                    .SetBorder(Border.NO_BORDER)
                                    .SetPaddingBottom(18));

                                // Row: Job Description
                                jobDetailsTable.AddCell(new Cell()
                                    .Add(new Paragraph("Job Description:")
                                        .SetFont(times)
                                        .SetFontSize(14))
                                    .SetPaddingLeft(56)
                                    .SetBorder(Border.NO_BORDER));

                                jobDetailsTable.AddCell(new Cell()
                                    .Add(new Paragraph(
                                        $"Towage approval for towage of {model.TowName} by {model.TugName} from {model.DeparturePort} to {model.ArrivalPort}.")
                                        .SetFont(times)
                                        .SetFontSize(14)
                                        .SetTextAlignment(TextAlignment.LEFT))
                                    .SetBorder(Border.NO_BORDER));

                                // Add the table to the document
                                document.Add(jobDetailsTable);



                                document.Add(new Paragraph("\n"));

                                // 📌 Add Stamp Image (if available)
                                string stampImagePath2 = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model.StampPath.TrimStart('/'));



                                document.Add(new Paragraph("\n\n"));

                                //if (System.IO.File.Exists(stampImagePath2))
                                //{
                                //    Image stampImage = new Image(ImageDataFactory.Create(stampImagePath2))
                                //        .SetWidth(120)
                                //        .SetHeight(100)
                                //        .SetTextAlignment(TextAlignment.LEFT);
                                //    document.Add(stampImage);
                                //}
                                //else
                                //{
                                //    document.Add(new Paragraph("").SetFontColor(iText.Kernel.Colors.ColorConstants.RED));
                                //}


                                // Aries and Client Representatives Table for Timesheet
                                Table timesheetTable1 = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 })).UseAllAvailableWidth();
                                timesheetTable1.SetMarginTop(5);  // Reduced top margin to move the table upward

                                // Aries Representative Cell
                                Cell ariesTimeCell1 = new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER);

                                // Empty space reduced between sections
                                document.Add(new Paragraph("\n\n\n")); // fewer newlines

                                // 📌 Add Stamp Image (if available)
                                if (System.IO.File.Exists(stampImagePath2))
                                {
                                    Image stampImage = new Image(ImageDataFactory.Create(stampImagePath2))
                                        .SetWidth(120)
                                        .SetHeight(100)
                                        .SetTextAlignment(TextAlignment.LEFT)
                                            .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin);
                                    document.Add(stampImage);
                                }
                                else
                                {
                                    document.Add(new Paragraph("").SetFontColor(iText.Kernel.Colors.ColorConstants.RED));
                                }

                                // Aries Representative Section
                                ariesTimeCell1.Add(new Paragraph("Aries Representative")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                    .SetUnderline()
                                    .SetFontSize(18)
                                    .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin)
);  // Reduced bottom margin for compactness

                                ariesTimeCell1.Add(new Paragraph($"Name:{model.SurveyedBy}")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                    .SetFontSize(18)
                                    .SetMarginTop(0)
                                        .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));  // Removed extra top margin

                                ariesTimeCell1.Add(new Paragraph("Signature:")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                    .SetFontSize(18)
                                    .SetMarginBottom(2)
                                        .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));  // Reduced bottom margin

                                if (System.IO.File.Exists(signaturePath1))
                                {
                                    Image signatureImageTime = new Image(ImageDataFactory.Create(signaturePath1))
                                        .SetWidth(80)
                                        .SetHeight(40);
                                    ariesTimeCell1.Add(signatureImageTime);
                                }

                                ariesTimeCell1.Add(new Paragraph($"Date:{model.IssuedDate}")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                    .SetFontSize(18)
                                    .SetMarginTop(2)
                                        .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin)); // Reduced top margin for tight spacing

                                timesheetTable1.AddCell(ariesTimeCell1);

                                // Client Representative Section
                                Cell clientTimeCell1 = new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER);

                                clientTimeCell1.Add(new Paragraph("Client Representative")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                    .SetUnderline()
                                    .SetFontSize(18)
                                    .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin)
);  // Reduced bottom margin for compactness

                                clientTimeCell1.Add(new Paragraph($"Name:")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                    .SetFontSize(18)
                                    .SetMarginTop(0)
                                        .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));  // Removed extra top margin

                                clientTimeCell1.Add(new Paragraph($"Company:{model.ClientName}")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                    .SetFontSize(18)
                                    .SetMarginBottom(2)
                                        .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));  // Reduced bottom margin

                                clientTimeCell1.Add(new Paragraph("Signature:")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                    .SetFontSize(18)
                                        .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));

                                string signaturePathTimeClient1 = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model.SignatureClient.TrimStart('/'));

                                if (System.IO.File.Exists(signaturePathTimeClient1))
                                {
                                    Image signatureImageClientTime = new Image(ImageDataFactory.Create(signaturePathTimeClient1))
                                        .SetWidth(80)
                                        .SetHeight(40);
                                    clientTimeCell1.Add(signatureImageClientTime);
                                }

                                clientTimeCell1.Add(new Paragraph($"Date:")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                    .SetFontSize(18)
                                    .SetMarginTop(2)
                                        .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin)); // Reduced top margin for tight spacing

                                timesheetTable1.AddCell(clientTimeCell1);

                                // Add the table to the document
                                document.Add(timesheetTable1);


                                // --- Footer registration ---
                                int lastPage2 = pdf.GetNumberOfPages();
                                pdf.AddEventHandler(PdfDocumentEvent.END_PAGE, new CustomFooterHandler(new[] { lastPage2 }));

                                // --- Restore original document margins (optional) ---
                                document.SetMargins(saveTop, saveRight, saveBot, saveLeft);




                                //=====================================================================================================================================================================================================================
                                // 📌 Add Page Break for new section
                                int timesheetPage = pdf.GetNumberOfPages() + 1;  // next page index
                                document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                                skipPages.Add(timesheetPage);                    // don't watermark this page

                                document.SetMargins(innerMargin, innerMargin, innerMargin, innerMargin);

                                PdfFont boldFont4 = PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD);
                                PdfFont normalFont3 = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);
                                // Step 2: Load your logo
                                string imagePath2 = System.IO.Path.Combine("wwwroot/images", "Aries.png");
                                Image logo1 = new Image(ImageDataFactory.Create(imagePath1)).ScaleToFit(120, 100);

                                // Step 3: Create a simple header table
                                Table headerTable1 = new Table(UnitValue.CreatePercentArray(new float[] { 1, 4 }))
                                    .UseAllAvailableWidth();

                                headerTable1.AddCell(new Cell().Add(logo)
                                    .SetBorder(iText.Layout.Borders.Border.NO_BORDER)
                                    .SetTextAlignment(TextAlignment.LEFT)
                                    .SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));

                                PdfFont boldFont2 = PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD);

                                headerTable1.AddCell(new Cell().Add(new Paragraph("Time Sheet")
                                        .SetFont(boldFont)
                                        .SetFontSize(20)
                                        .SetTextAlignment(TextAlignment.RIGHT))
                                    .SetBorder(iText.Layout.Borders.Border.NO_BORDER)
                                    .SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));


                                document.Add(headerTable1);  // ✅ Now add header table
                                document.Add(new Paragraph("\n"));

                                // Client & Job Details Table
                                Table detailsTable = new Table(new float[] { 150f, 350f }); // Fixed width for left column
                                detailsTable.SetWidth(UnitValue.CreatePercentValue(100)).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin);
                                ;

                                detailsTable.AddCell(new Cell().Add(new Paragraph("CLIENT").SetFontSize(15).SetFont(boldFont)));
                                detailsTable.AddCell(new Cell().Add(new Paragraph(model.ClientName).SetFontSize(15).SetFont(normalFont)));

                                detailsTable.AddCell(new Cell().Add(new Paragraph("VESSEL/PROJECT NAME").SetFontSize(15).SetFont(boldFont)));
                                detailsTable.AddCell(new Cell().Add(new Paragraph(model.ProjectTitle).SetFontSize(15).SetFont(normalFont)));

                                detailsTable.AddCell(new Cell().Add(new Paragraph("LOCATION").SetFontSize(15).SetFont(boldFont)));
                                detailsTable.AddCell(new Cell().Add(new Paragraph(model.DeparturePort).SetFontSize(15).SetFont(normalFont)));

                                detailsTable.AddCell(new Cell().Add(new Paragraph("NAME OF PERSON").SetFontSize(15).SetFont(boldFont)));
                                detailsTable.AddCell(new Cell().Add(new Paragraph(model.SurveyedBy).SetFontSize(15).SetFont(normalFont)));

                                detailsTable.AddCell(new Cell().Add(new Paragraph("TIME SHEET NO").SetFontSize(15).SetFont(boldFont)));
                                detailsTable.AddCell(new Cell().Add(new Paragraph("01").SetFontSize(15).SetFont(normalFont)));

                                detailsTable.AddCell(new Cell().Add(new Paragraph("JOB NO").SetFontSize(15).SetFont(boldFont)));
                                detailsTable.AddCell(new Cell().Add(new Paragraph(model.JobNumber).SetFontSize(15).SetFont(normalFont)));

                                detailsTable.AddCell(new Cell().Add(new Paragraph("TYPE OF JOB").SetFontSize(15).SetFont(boldFont)));
                                detailsTable.AddCell(new Cell().Add(new Paragraph(" TOWAGE APPROVAL").SetFontSize(15).SetFont(normalFont))); // use correct model field

                                document.Add(detailsTable);
                                //document.Add(new Paragraph("\n"));
                                // ---------- TIMESHEET TABLE ----------
                                Table timeSheetTable = new Table(UnitValue.CreatePointArray(new float[] {
    90f,   // DATE
    55f,   // TIME FROM
    55f,   // TIME TO
    80f,   // TOTAL HOURS
    240f   // REMARKS (made wider)
}))
                                .UseAllAvailableWidth()
                                .SetMarginLeft(innerMargin)
                                .SetMarginRight(innerMargin);

                                // ---------- HEADER ROWS ----------

                                // First Row of Header
                                timeSheetTable.AddCell(new Cell(2, 1)
                                    .Add(new Paragraph("DATE")
                                    .SetFontSize(13)
                                    .SetFont(boldFont)
                                    .SetTextAlignment(TextAlignment.CENTER)
                                    .SetVerticalAlignment(VerticalAlignment.MIDDLE)));

                                timeSheetTable.AddCell(new Cell(1, 2)
                                    .Add(new Paragraph("TIME")
                                    .SetFontSize(13)
                                    .SetFont(boldFont)
                                    .SetTextAlignment(TextAlignment.CENTER))
                                    .SetVerticalAlignment(VerticalAlignment.MIDDLE));

                                timeSheetTable.AddCell(new Cell(2, 1)
                                    .Add(new Paragraph("TOTAL HOURS")
                                    .SetFontSize(13)
                                    .SetFont(boldFont)
                                    .SetTextAlignment(TextAlignment.CENTER)
                                    .SetVerticalAlignment(VerticalAlignment.MIDDLE)));

                                timeSheetTable.AddCell(new Cell(2, 1)
                                    .Add(new Paragraph("REMARKS")
                                    .SetFontSize(13)
                                    .SetFont(boldFont)
                                    .SetTextAlignment(TextAlignment.CENTER)
                                    .SetVerticalAlignment(VerticalAlignment.MIDDLE)));

                                // Second Row (TIME subheaders)
                                timeSheetTable.AddCell(new Cell()
                                    .Add(new Paragraph("FROM")
                                    .SetFontSize(13)
                                    .SetFont(boldFont)
                                    .SetTextAlignment(TextAlignment.CENTER)
                                    .SetVerticalAlignment(VerticalAlignment.MIDDLE)));

                                timeSheetTable.AddCell(new Cell()
                                    .Add(new Paragraph("TO")
                                    .SetFontSize(13)
                                    .SetFont(boldFont)
                                    .SetTextAlignment(TextAlignment.CENTER)
                                    .SetVerticalAlignment(VerticalAlignment.MIDDLE)));

                                // ---------- EMPTY ENTRY ROWS ----------
                                for (int i = 0; i < 10; i++)
                                {
                                    for (int j = 0; j < 5; j++)
                                    {
                                        timeSheetTable.AddCell(new Cell()
                                            .Add(new Paragraph(" "))
                                            .SetMinHeight(18)
                                            .SetTextAlignment(TextAlignment.LEFT));
                                    }
                                }

                                // ---------- CONSUMABLES SECTION ----------
                                timeSheetTable.AddCell(new Cell(1, 1)
                                    .Add(new Paragraph("Consumables")
                                    .SetFontSize(13)
                                    .SetFont(normalFont)
                                    .SetTextAlignment(TextAlignment.LEFT)
                                    .SetVerticalAlignment(VerticalAlignment.MIDDLE)));

                                timeSheetTable.AddCell(new Cell(1, 4)
                                    .Add(new Paragraph(" "))
                                    .SetMinHeight(18));

                                // Add 2 empty rows below Consumables
                                for (int i = 0; i < 2; i++)
                                {
                                    timeSheetTable.AddCell(new Cell(1, 5)
                                        .Add(new Paragraph(" "))
                                        .SetMinHeight(22));
                                }

                                // ---------- ADD TABLE TO DOCUMENT ----------
                                document.Add(timeSheetTable);


                                // Aries and Client Representatives Table for Timesheet
                                Table timesheetTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 })).UseAllAvailableWidth();
                                timesheetTable.SetMarginTop(5);  // Reduced top margin to move the table upward

                                // Aries Representative Cell
                                Cell ariesTimeCell = new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER);

                                // Empty space reduced between sections
                                document.Add(new Paragraph("\n\n\n")); // fewer newlines

                                // 📌 Add Stamp Image (if available)
                                if (System.IO.File.Exists(stampImagePath2))
                                {
                                    Image stampImage = new Image(ImageDataFactory.Create(stampImagePath2))
                                        .SetWidth(120)
                                        .SetHeight(100)
                                        .SetTextAlignment(TextAlignment.LEFT)
                                            .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin);
                                    document.Add(stampImage);
                                }
                                else
                                {
                                    document.Add(new Paragraph("").SetFontColor(iText.Kernel.Colors.ColorConstants.RED));
                                }

                                // Aries Representative Section
                                ariesTimeCell.Add(new Paragraph("Aries Representative")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                    .SetUnderline()
                                    .SetFontSize(18)
                                    .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin)
);  // Reduced bottom margin for compactness

                                ariesTimeCell.Add(new Paragraph($"Name:")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                    .SetFontSize(18)
                                    .SetMarginTop(0)
                                        .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));  // Removed extra top margin

                                ariesTimeCell.Add(new Paragraph("Signature:")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                    .SetFontSize(18)
                                    .SetMarginBottom(2)
                                        .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));  // Reduced bottom margin

                                if (System.IO.File.Exists(signaturePath1))
                                {
                                    Image signatureImageTime = new Image(ImageDataFactory.Create(signaturePath1))
                                        .SetWidth(80)
                                        .SetHeight(40);
                                    ariesTimeCell.Add(signatureImageTime);
                                }

                                ariesTimeCell.Add(new Paragraph($"Date:{model.IssuedDate}")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                    .SetFontSize(18)
                                    .SetMarginTop(2)
                                        .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin)); // Reduced top margin for tight spacing

                                timesheetTable.AddCell(ariesTimeCell);

                                // Client Representative Section
                                Cell clientTimeCell = new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER);

                                clientTimeCell.Add(new Paragraph("Client Representative")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                    .SetUnderline()
                                    .SetFontSize(18)
                                    .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin)
);  // Reduced bottom margin for compactness

                                clientTimeCell.Add(new Paragraph($"Name:")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                    .SetFontSize(18)
                                    .SetMarginTop(0)
                                        .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));  // Removed extra top margin

                                clientTimeCell.Add(new Paragraph($"Company:{model.ClientName}")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                    .SetFontSize(18)
                                    .SetMarginBottom(2)
                                        .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));  // Reduced bottom margin

                                clientTimeCell.Add(new Paragraph("Signature:")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                    .SetFontSize(18)
                                        .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));

                                string signaturePathTimeClient = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model.SignatureClient.TrimStart('/'));

                                if (System.IO.File.Exists(signaturePathTimeClient))
                                {
                                    Image signatureImageClientTime = new Image(ImageDataFactory.Create(signaturePathTimeClient))
                                        .SetWidth(80)
                                        .SetHeight(40);
                                    clientTimeCell.Add(signatureImageClientTime);
                                }

                                clientTimeCell.Add(new Paragraph($"Date:")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                    .SetFontSize(18)
                                    .SetMarginTop(2)
                                        .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin)); // Reduced top margin for tight spacing

                                timesheetTable.AddCell(clientTimeCell);

                                // Add the table to the document
                                document.Add(timesheetTable);

                                // Track page after adding all content
                                int lastPage3 = pdf.GetNumberOfPages(); // after adding all content
                                pdf.AddEventHandler(PdfDocumentEvent.END_PAGE, new CustomFooterHandler(new[] { lastPage3 }));



                                //=================================================================================================================================================================
                                document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                                int checklistStartPage = pdf.GetNumberOfPages();
                                // Create checklistTable here (as usual, no Aries header at top)


                                // Add logo
                                string logoPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "Logo.png");
                                if (System.IO.File.Exists(logoPath))
                                {
                                    Image logo5 = new Image(ImageDataFactory.Create(logoPath));
                                    document.Add(logo5);
                                }
                                // Aries header table
                                Table headerTable9 = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 })).UseAllAvailableWidth();
                                headerTable9.AddCell(new Cell().Add(new Paragraph($"").SetFont(boldFont4)).SetBorder(iText.Layout.Borders.Border.NO_BORDER));
                                headerTable9.AddCell(new Cell().Add(new Paragraph(model.CertificateType).SetFontSize(15).SetFont(boldFont4).SetTextAlignment(TextAlignment.RIGHT)).SetBorder(iText.Layout.Borders.Border.NO_BORDER));
                                document.Add(headerTable9);



                                // Font
                                PdfFont boldFont6 = PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD);
                                PdfFont normalFont4 = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);

                                // Aries header table
                                Table headerTable3 = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 })).UseAllAvailableWidth();
                                headerTable3.AddCell(new Cell().Add(new Paragraph("ARIES SITE CHECKLIST").SetFontSize(15).SetFont(boldFont6)));
                                headerTable3.AddCell(new Cell().Add(new Paragraph("TOWAGE SURVEY").SetFontSize(15).SetFont(normalFont4).SetTextAlignment(TextAlignment.LEFT)));
                                document.Add(headerTable3);

                                // Project info table
                                Table projectTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 })).UseAllAvailableWidth();
                                projectTable.AddCell(new Cell().Add(new Paragraph($"PROJECT NAME: {model.ProjectTitle}").SetFontSize(15).SetFont(normalFont4)));
                                projectTable.AddCell(new Cell().Add(new Paragraph($"CoA No:{model.JobNumber} / {model.CertificateNumber}").SetFontSize(15).SetFont(normalFont4)));
                                projectTable.AddCell(new Cell().Add(new Paragraph("Route: STEVIN ROCK JETTY, RAS AL KHAIMAH, UAE to BAHRAIN").SetFontSize(15).SetFont(normalFont4)));
                                projectTable.AddCell(new Cell().Add(new Paragraph($"Job No: {model.JobNumber}").SetFontSize(15).SetFont(normalFont4)));
                                document.Add(projectTable);


                                // Vessel Table
                                Table vesselTable = new Table(UnitValue.CreatePercentArray(new float[] { 2, 2, 2, 2 })).UseAllAvailableWidth();
                                vesselTable.AddHeaderCell(new Cell().Add(new Paragraph("Vessel").SetFontSize(15).SetFont(boldFont6).SetBackgroundColor(ColorConstants.LIGHT_GRAY)));
                                vesselTable.AddHeaderCell(new Cell().Add(new Paragraph("Name").SetFontSize(15).SetFont(boldFont6).SetBackgroundColor(ColorConstants.LIGHT_GRAY)));
                                vesselTable.AddHeaderCell(new Cell().Add(new Paragraph("IMO No. if any / flag").SetFontSize(15).SetFont(boldFont6).SetBackgroundColor(ColorConstants.LIGHT_GRAY)));
                                vesselTable.AddHeaderCell(new Cell().Add(new Paragraph("Survey report no. & date").SetFontSize(15).SetFont(boldFont6).SetBackgroundColor(ColorConstants.LIGHT_GRAY)));

                                vesselTable.AddCell(new Cell().Add(new Paragraph("Transport ship or barge").SetFontSize(15).SetFont(normalFont4)));
                                vesselTable.AddCell(new Cell().Add(new Paragraph(model.TugName).SetFontSize(15).SetFont(normalFont4)));
                                vesselTable.AddCell(new Cell());
                                vesselTable.AddCell(new Cell());

                                vesselTable.AddCell(new Cell().Add(new Paragraph("Towing vessel 1").SetFontSize(15).SetFont(normalFont4)));
                                vesselTable.AddCell(new Cell().Add(new Paragraph(model.TowName).SetFontSize(15).SetFont(normalFont4)));
                                vesselTable.AddCell(new Cell());
                                vesselTable.AddCell(new Cell());

                                vesselTable.AddCell(new Cell().Add(new Paragraph("Towing vessel 2").SetFontSize(15).SetFont(normalFont4)));
                                vesselTable.AddCell(new Cell());
                                vesselTable.AddCell(new Cell());
                                vesselTable.AddCell(new Cell());

                                document.Add(vesselTable);

                                // ----------- CHECKLIST TABLE 1 -----------
                                Table checklistTable = new Table(UnitValue.CreatePercentArray(new float[] { 1.0f, 5.0f, 1.5f, 3.5f })) // Status smaller, Comments wider
                                    .UseAllAvailableWidth();

                                checklistTable.AddHeaderCell(new Cell().Add(new Paragraph("1")
                                    .SetFont(boldFont6)
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)));

                                checklistTable.AddHeaderCell(new Cell().Add(new Paragraph("FOR ALL TYPES OF TOW/TRANSPORT")
                                    .SetFontSize(15)
                                    .SetFont(boldFont6)
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)));

                                checklistTable.AddHeaderCell(new Cell().Add(new Paragraph("STATUS (Tick or NA)")
                                    .SetFont(boldFont6)
                                    .SetFontSize(15)
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetTextAlignment(TextAlignment.CENTER)));

                                checklistTable.AddHeaderCell(new Cell().Add(new Paragraph("Comments")
                                    .SetFont(boldFont6)
                                    .SetFontSize(15)
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetTextAlignment(TextAlignment.CENTER)));

                                checklistTable.AddCell(new Cell(1, 4)
                                    .Add(new Paragraph("Condition & (de)ballasting")
                                    .SetFontSize(14)
                                    .SetFont(boldFont6)
                                    .SetBackgroundColor(ColorConstants.GRAY)
                                    .SetFontColor(ColorConstants.WHITE)));

                                // your loop for items1 here remains unchanged

                                string[,] items1 = {
    { "1.1", "Documentation & outstanding recommendations for (towed) hull & equipment.", "", "" },
    { "1.2", "Condition of (towed) hull & equipment.", "", "" },
    { "1.3", "Watertight integrity of (towed) hull, deck openings, valves, vents, portholes & closures.", "", "" },
    { "1.4", "Confirm adequate means for pumping out water ingress & counter-flooding.", "", "" },
    { "1.5", "Green water protection of cargo (/tow connection).", "", "" },
    { "1.6", "Final loading/ballast condition of (towed) vessel checked for stability and in agreement with design condition (draught, trim etc). All residual ballast water stripped out.", "", "" }
};

                                for (int i = 0; i < items1.GetLength(0); i++)
                                {
                                    for (int j = 0; j < 4; j++)
                                    {
                                        checklistTable.AddCell(new Cell().Add(new Paragraph(items1[i, j]).SetFontSize(13).SetFont(normalFont4)));
                                    }
                                }

                                checklistTable.AddCell(new Cell(1, 4)
               .Add(new Paragraph("Seafastening / storage").SetFontSize(15)
               .SetFont(boldFont)
               .SetFontColor(ColorConstants.WHITE)
               .SetBackgroundColor(ColorConstants.GRAY)));

                                string[,] items2 = {
    { "1.7", "Correct layout of seafastenings, supports and grillage etc. (pre-loadout).", "", "" },
    { "1.8", "Check on seafastening design changes or agreed site modifications.", "", "" },
    { "1.9", "Integrity of cargo and seafastening to hull/deck. Material Certs, Weld procedures, Welder qualifications, NDT Certs as appropriate (Sea fastenings to be welded at final ballast condition).", "", "" },
    { "1.10", "Sea fastening / storing of shipped loose items including any wire slings, rigging & shackles tight as appropriate.", "", "" },
    { "1.11", "Internal sea fastening & no loose items that can drop e.g. during lift. Barge and module swept clean.", "", "" },
    { "1.12", "Cranes and derricks secured.", "", "" },
    { "1.13", "Security of dangerous goods.", "", "" },
    { "1.14", "Protection of pressurized tanks or receptacles.", "", "" }
};

                                for (int i = 0; i < items2.GetLength(0); i++)
                                {
                                    for (int j = 0; j < 4; j++)
                                    {
                                        checklistTable.AddCell(new Cell().Add(new Paragraph(items2[i, j]).SetFont(normalFont4)));
                                    }
                                }

                                document.Add(checklistTable);




                                Table checklistTable2 = new Table(UnitValue.CreatePercentArray(new float[] { 1.0f, 5.0f, 1.5f, 3.5f })); // Status smaller, Comments wider

                                string[,] items4 = {
    { "1.15", "Legs, cantilever, water tower etc. confirmed as at correct height /position.", "", "" },
    { "1.16", "Light & signals (international & local lights & shapes – with adequate spares).", "", "" },
    { "1.17", "Pilotage, assisting tugs etc. required for departure with hand-overs defined.", "", "" },
    { "1.18", "Guard /escort vessel (for any phases).", "", "" },
    { "1.19", "No head room (air draught) restriction for voyage/tow.", "", "" },
    { "1.20", "Marine procedures including weather forecasting and/or routing advisory service to be provided for the voyage & discussed with Master.", "", "" },

    { "1.21", "Weather conditions and forecast suitable for departure.", "", "" },
    { "1.22", "Agreed progress reporting system established between the vessel and the cargo owners.", "", "" },
    { "1.23", "Manning - LSA, FFA, accommodation, communications etc.", "", "" }
};


                                string[,] items5 = {
    { "1.24", "Riding crew, if carried, or HL vessel crew briefed for regular checks and fire watch throughout voyage.", "", "" },
    { "1.25", "Access to towed hulls, boarding ladders etc.", "", "" },
                                   { "1.26", " Final check of cargo to ensure all\r\n equipment internally and externally is\r\n secure.", "", "" },
                                   {"1.27"," Dimensional check on grillage and\r\n seafastening to  ensure design changes\r\n and agreed site modifications have been\r\n implemented.","","" },
                                    {"1.28","Verify post loadout re-instatement\r\n completed OK & re classed if necessary.","","" },

};

                                checklistTable2.AddCell(new Cell(1, 4)
                                    .Add(new Paragraph("Miscellaneous")
                                    .SetFontSize(15)
                                    .SetFont(boldFont6)
                                    .SetBackgroundColor(ColorConstants.GRAY)
                                    .SetFontColor(ColorConstants.WHITE)));
                                for (int i = 0; i < items4.GetLength(0); i++)
                                {
                                    for (int j = 0; j < 4; j++)
                                    {
                                        checklistTable2.AddCell(new Cell().Add(new Paragraph(items4[i, j]).SetFont(normalFont4)));
                                    }
                                }


                                checklistTable2.AddCell(new Cell(1, 4)
     .Add(new Paragraph("Final checks")
     .SetFontSize(15)
     .SetFont(boldFont6)
     .SetBackgroundColor(ColorConstants.GRAY)
     .SetFontColor(ColorConstants.WHITE)));

                                for (int i = 0; i < items5.GetLength(0); i++)
                                {
                                    for (int j = 0; j < 4; j++)
                                    {
                                        checklistTable2.AddCell(new Cell().Add(new Paragraph(items5[i, j]).SetFont(normalFont4)));
                                    }
                                }



                                // Add "FOR TOWAGES ONLY" section
                                checklistTable2.AddCell(new Cell(1, 4)
                                    .Add(new Paragraph("2 FOR TOWAGES ONLY")).SetFontSize(15)
                                    .SetFont(boldFont)
                                    .SetFontColor(ColorConstants.WHITE)
                                    .SetBackgroundColor(ColorConstants.GRAY));

                                // Optional: add column headers for this section (if needed)
                                checklistTable2.AddCell(new Cell().Add(new Paragraph("No.").SetFontSize(15).SetFont(boldFont6)));
                                checklistTable2.AddCell(new Cell().Add(new Paragraph("").SetFontSize(15).SetFont(boldFont6)));
                                checklistTable2.AddCell(new Cell().Add(new Paragraph("OK (Tick or NA or initials if more than 1 surveyor")).SetFontSize(15).SetFont(boldFont6)); // Empty or custom
                                checklistTable2.AddCell(new Cell().Add(new Paragraph("Comments)").SetFontSize(15).SetFont(boldFont6)));

                                // Add rows for FOR TOWAGES ONLY
                                string[,] items6 = {
    { "2.1", "Towing vessel(s) - equipment, supplies, manning & documentation (see Tug report form).", "", "" },
    { "2.2", "Towing vessel(s) - outstanding recommendations.", "", "" },
    { "2.3", "Towing connections - strength construction and position.", "", "" },
    { "2.4", "Towing gear strength, composition & condition.", "", "" },
    { "2.5", "Towing gear fairleads (including chafe).", "", "" },
    { "2.6", "Towing gear retrieval systems.", "", "" },
    { "2.7", "Emergency tow gear (as above).", "", "" },
    { "2.8", "Emergency anchoring systems. Strength, holding & deployment system.", "", "" },
    { "2.9", "Adequate fuel (& contingencies) on board. Confirm refueling arrangements if required.", "", "" },
    { "2.10", "Power / adequate fuel for navigation lights, lights and shapes in position on tow & tested.", "", "" },
    { "2.11", "All watertight openings closed on departure & ballast system secured (unmanned tows only).", "", "" }

};



                                for (int i = 0; i < items6.GetLength(0); i++)
                                {
                                    for (int j = 0; j < 4; j++)
                                    {
                                        checklistTable2.AddCell(new Cell().Add(new Paragraph(items6[i, j]).SetFont(normalFont4)));
                                    }
                                }
                                // 2.12 row: number + description
                                checklistTable2.AddCell(new Cell().Add(new Paragraph("2.12").SetFont(normalFont4)));

                                checklistTable2.AddCell(new Cell()
                                    .Add(new Paragraph("Barge departure draughts. Please specify if in feet or meters and comment on readability of draught marks")
                                        .SetFont(normalFont4)));

                                // --- draughts mini-table (3 columns: label | Port | Starboard) ---
                                var lineBorder = new SolidBorder(ColorConstants.BLACK, 0.5f);
                                var noBorder = Border.NO_BORDER;
                                float rowMinH = 16f; // visual height for empty inputs

                                Table draughtTable = new Table(UnitValue.CreatePercentArray(new float[] { 1.2f, 1f, 1f }))
                                    .UseAllAvailableWidth()
                                    .SetPadding(0)
                                    .SetMargin(0);

                                // Header row: empty corner + Port + Starboard (shaded)
                                draughtTable.AddCell(new Cell()
                                    .Add(new Paragraph("")) // top-left corner
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetBorder(lineBorder));

                                draughtTable.AddCell(new Cell()
                                    .Add(new Paragraph("Port").SetFont(boldFont6))
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetTextAlignment(TextAlignment.CENTER)
                                    .SetBorder(lineBorder));

                                draughtTable.AddCell(new Cell()
                                    .Add(new Paragraph("Starboard").SetFont(boldFont6))
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetTextAlignment(TextAlignment.CENTER)
                                    .SetBorder(lineBorder));

                                // Helper to add one data row (Forward/Aft/Mean)
                                void DraughtRow(string label)
                                {
                                    draughtTable.AddCell(new Cell()
                                        .Add(new Paragraph(label).SetFont(normalFont4))
                                        .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                        .SetBorder(lineBorder));

                                    draughtTable.AddCell(new Cell()
                                        .Add(new Paragraph(" ")) // empty input cell
                                        .SetMinHeight(rowMinH)
                                        .SetBorder(lineBorder));

                                    draughtTable.AddCell(new Cell()
                                        .Add(new Paragraph(" "))
                                        .SetMinHeight(rowMinH)
                                        .SetBorder(lineBorder));
                                }

                                DraughtRow("Forward");
                                DraughtRow("Aft");
                                DraughtRow("Mean");
                                // Units: label + spanning input (baseline + left/right borders)
                                draughtTable.AddCell(new Cell()
                                    .Add(new Paragraph("Units:").SetFont(normalFont4))
                                    .SetBorder(lineBorder));

                                draughtTable.AddCell(new Cell(1, 2)
                                    .Add(new Paragraph(" "))
                                    .SetMinHeight(rowMinH - 2)
                                    .SetBorder(lineBorder)         // add all sides
                                    .SetBorderTop(noBorder));      // keep only baseline visible on top

                                // Readability: label + spanning input (baseline + left/right borders)
                                draughtTable.AddCell(new Cell()
                                    .Add(new Paragraph("Readability:").SetFont(normalFont4))
                                    .SetBorder(lineBorder));

                                draughtTable.AddCell(new Cell(1, 2)
                                    .Add(new Paragraph(" "))
                                    .SetMinHeight(rowMinH - 2)
                                    .SetBorder(lineBorder)         // add all sides
                                    .SetBorderTop(noBorder));      // keep only baseline visible on top


                                // Place the mini-table into the last two columns of the outer table
                                checklistTable2.AddCell(
                                    new Cell(1, 2)          // span the last two columns of the 4-col checklist table
                                        .Add(draughtTable)
                                        .SetPadding(0)      // important to align borders tightly
                                        .SetBorder(noBorder) // outer cell border usually off; keep if you want a box
                                );


                                // 2.13 & 2.14 (regular rows)
                                string[,] continuation = {
    { "2.13", "Tug master given barge departure ballast plan & draughts.", "", "" },
    { "2.14", "Tug master and chief engineer shown barge towing connections and pump room", "", "" }
};

                                for (int i = 0; i < continuation.GetLength(0); i++)
                                {
                                    for (int j = 0; j < 4; j++)
                                    {
                                        checklistTable2.AddCell(new Cell().Add(new Paragraph(continuation[i, j]).SetFont(normalFont4)));
                                    }
                                }


                                // Section 3: ADDITIONAL FOR TOWED SHIPS ONLY
                                checklistTable2.AddCell(new Cell(1, 4)
                                    .Add(new Paragraph("3 ADDITIONAL FOR TOWED SHIPS ONLY")).SetFontSize(15)
                                    .SetFont(boldFont)
                                    .SetFontColor(ColorConstants.WHITE)
                                    .SetBackgroundColor(ColorConstants.GRAY));

                                // Header row
                                checklistTable2.AddCell(new Cell().Add(new Paragraph("")));
                                checklistTable2.AddCell(new Cell());
                                checklistTable2.AddCell(new Cell().Add(new Paragraph("OK (Tick or NA or initials if more than 1 surveyor)").SetFont(boldFont6)));
                                checklistTable2.AddCell(new Cell().Add(new Paragraph("Comments").SetFont(boldFont6)));



                                string[,] section3 = {
    { "3.1", "Ensure rudder immobilized in amidships position.", "", "" },
    { "3.2", "Confirm propeller shaft disconnected.", "", "" },
    { "3.3", "Check if any (additional) cargo loaded or to be loaded.", "", "" }
};

                                document.Add(new Paragraph("\n"));
                                for (int i = 0; i < section3.GetLength(0); i++)
                                {
                                    for (int j = 0; j < 4; j++)
                                    {
                                        checklistTable2.AddCell(new Cell().Add(new Paragraph(section3[i, j]).SetFont(normalFont4)));
                                    }
                                }

                                // Section 4: For FLOATED ON cargoes (e.g. MODUs) only
                                checklistTable2.AddCell(new Cell(1, 4)
                                    .Add(new Paragraph("4 For FLOATED ON cargoes (e.g. MODUs) only")).SetFontSize(15)
                                    .SetFont(boldFont)
                                    .SetFontColor(ColorConstants.WHITE)
                                    .SetBackgroundColor(ColorConstants.GRAY));

                                checklistTable2.AddCell(new Cell().Add(new Paragraph("")));
                                checklistTable2.AddCell(new Cell().Add(new Paragraph("").SetFont(boldFont6)));
                                checklistTable2.AddCell(new Cell().Add(new Paragraph("Ok").SetFont(boldFont6)));
                                checklistTable2.AddCell(new Cell().Add(new Paragraph("Comments").SetFont(boldFont6)));

                                string[,] section4 = {
    { "4.1", "Cargo/MODU correctly positioned against guide posts.", "", "" },
    { "4.2", "Cargo/MODU seated on cribbing and adequately supported.", "", "" },
    { "4.3", "All seafastenings correctly positioned. All stoppers jacked up tight against Cargo/MODU bulkheads. All welds with throat thicknesses in accordance with the transportation manual/approved drawings.", "", "" }
};

                                for (int i = 0; i < section4.GetLength(0); i++)
                                {
                                    for (int j = 0; j < 4; j++)
                                    {
                                        checklistTable2.AddCell(new Cell().Add(new Paragraph(section4[i, j]).SetFont(normalFont4)));
                                    }
                                }

                                // Section 5: FOR CARGOS ON HL VESSELS (e.g. MODUS) ONLY
                                checklistTable2.AddCell(new Cell(1, 4)
                                    .Add(new Paragraph("5 FOR CARGOS ON HL VESSELS (e.g. MODUS) ONLY")).SetFontSize(15)
                                    .SetFont(boldFont)
                                    .SetFontColor(ColorConstants.WHITE)
                                    .SetBackgroundColor(ColorConstants.GRAY));

                                checklistTable2.AddCell(new Cell().Add(new Paragraph("")));
                                checklistTable2.AddCell(new Cell());
                                checklistTable2.AddCell(new Cell().Add(new Paragraph("Ok").SetFont(boldFont6)));
                                checklistTable2.AddCell(new Cell().Add(new Paragraph("Comments").SetFont(boldFont6)));

                                string[,] section5 = {
    { "5.1", "Safe and adequate access between the HL vessel and the cargo to be provided for the transportation passage. Provision of lifelines for the event of adverse weather.", "", "" }

    };

                                for (int i = 0; i < section5.GetLength(0); i++)
                                {
                                    for (int j = 0; j < 4; j++)
                                    {
                                        checklistTable2.AddCell(new Cell().Add(new Paragraph(section5[i, j]).SetFont(normalFont4)));
                                    }
                                }

                                // ➕ Section 5 continued (already started with 5.1 earlier)
                                string[,] section5_continued = {
    { "5.2", "Where applicable, firefighting water to be connected from the HL vessel to the cargo main fire system.", "", "" },
    { "5.3", "Earth wire or method of earthing connected between the cargo and the HL vessel.", "", "" }

    };

                                for (int i = 0; i < section5_continued.GetLength(0); i++)
                                {
                                    for (int j = 0; j < 4; j++)
                                    {
                                        checklistTable2.AddCell(new Cell().Add(new Paragraph(section5_continued[i, j]).SetFont(normalFont4)));
                                    }
                                }

                                // ➕ Section 6: FOR CARGO TO BE LIFTED ONLY
                                checklistTable2.AddCell(new Cell(1, 4)
                                    .Add(new Paragraph("6 FOR CARGO TO BE LIFTED ONLY")).SetFontSize(15)
                                    .SetFont(boldFont)
                                    .SetFontColor(ColorConstants.WHITE)
                                    .SetBackgroundColor(ColorConstants.GRAY));

                                // Header row for section 6
                                checklistTable2.AddCell(new Cell().Add(new Paragraph("")));
                                checklistTable2.AddCell(new Cell());
                                checklistTable2.AddCell(new Cell().Add(new Paragraph("Ok").SetFontSize(15).SetFont(boldFont6)));
                                checklistTable2.AddCell(new Cell().Add(new Paragraph("Comments").SetFontSize(15).SetFont(boldFont6)));

                                string[,] section6 = {
    { "6.1", "Correct Layout of Spreaders etc (Pre loadout)", "", "" },
    { "6.2", "Correct Installation of Restraint Material (Post loadout)", "", "" },
    { "6.3", "Wire rigging and shackles as appropriate (checked against certificates?)", "", "" }

};

                                for (int i = 0; i < section6.GetLength(0); i++)
                                {
                                    for (int j = 0; j < 4; j++)
                                    {
                                        checklistTable2.AddCell(new Cell().Add(new Paragraph(section6[i, j]).SetFont(normalFont4)));
                                    }
                                }

                                checklistTable2.AddCell(new Cell(1, 3)
                                .Add(new Paragraph("All CoA items signed off by\n(Attending Surveyor):\n\nChecklist authorised by\n(Project Manager or authorizer)").SetFontSize(15).SetFont(normalFont4)));
                                                                        
                                checklistTable2.AddCell(new Cell()
                                .Add(new Paragraph("Date").SetFont(normalFont4)));




                                document.Add(checklistTable2);
                                int checklistEndPage = pdf.GetNumberOfPages();

                                document.Close();
                                if (string.IsNullOrEmpty(model.JobNumber) || string.IsNullOrEmpty(model.CertificateNumber))
                                    return BadRequest("Missing required fields");

                                var pdfBytes = outputStream.ToArray();
                                _db.certificates.Add(model);
                                await _db.SaveChangesAsync();

                                var reportFolder = Path.Combine(
                                    Directory.GetCurrentDirectory(),
                                    "wwwroot",
                                    "reports",
                                    "certificates",
                                    "towage");
                                Directory.CreateDirectory(reportFolder);

                                var reportPath = Path.Combine(reportFolder, $"towage-{model.Id}.pdf");
                                await System.IO.File.WriteAllBytesAsync(reportPath, pdfBytes);
                                model.PdfFilePath = reportPath;
                                await _db.SaveChangesAsync();

                                return File(pdfBytes, "application/pdf", "Certificate.pdf");



                            }

                            // Return PDF as FileResponse
                        }

                    }
                }

                catch (Exception ex)
                {

                     return StatusCode(500,$"Error generating PDF:{ex.GetBaseException().Message}");

                }

            }
        }
    }
}


    
