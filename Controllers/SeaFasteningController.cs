using iText.Commons.Actions.Contexts;
using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Events;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Draw;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Layout;
using iText.Layout.Properties;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Surevey_Certification_App.Controller;
using Survey_Certification_App.Data;
using Survey_Certification_App.DTOS;
using Survey_Certification_App.Models;
using Path = System.IO.Path;

namespace Survey_Certification_App.Controllers
{


    [EnableCors("AllowAll")]
    [Route("api/[controller]")]
    [ApiController]
    public class SeaFasteningController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public SeaFasteningController(ApplicationDbContext db)
        {
            _db = db;
        }
        [HttpPost("upload-stamp")]
        public async Task<IActionResult> UploadStamp(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("  ");

            var folderPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "stamps");
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            var fileName = Guid.NewGuid() + System.IO.Path.GetExtension(file.FileName);
            var fullPath = System.IO.Path.Combine(folderPath, fileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var relativePath = "/stamps/" + fileName;
            return Ok(new { filePath = relativePath });
        }


        [HttpPost("upload-signature")]
        public async Task<IActionResult> UploadSignature(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("");

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
                return BadRequest("");

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




        [HttpPost("create-seafastening")]
        public async Task<IActionResult> CreateSeaFasteningCertificate([FromBody] SeaFastening model)
        {


            // 🔐 Failsafe fallback to prevent null or binding issues


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

                //// ✅ Hide IssuedDate if NA checkbox is checked
                //if (model.date)
                //{
                //    model.IssuedDate = null;
                //}

                try
                {
                    using (var outputStream = new MemoryStream())
                    {
                        using (var writer = new PdfWriter(outputStream))
                        {
                            using (var pdf = new iText.Kernel.Pdf.PdfDocument(writer))
                            {
                                //// ✅ Manually cadd a page first
                                var skipPages = new HashSet<int>();

                                // Step 2: Add handler
                                var watermarkHandler = new WatermarkEventHandler(skipPages);
                                pdf.AddEventHandler(PdfDocumentEvent.END_PAGE, watermarkHandler);


                                var document = new Document(pdf, iText.Kernel.Geom.PageSize.A3); // OR PageSize.A4
                                document.SetMargins(145, 50, 100, 50);


                                pdf.AddEventHandler(PdfDocumentEvent.END_PAGE, new PageBorderHandler());




                                // ✅ Load dynamic header image from model.HeaderImagePath
                                string imagePath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model.HeaderImagePath.TrimStart('/'));
                                if (System.IO.File.Exists(imagePath))
                                {
                                    var pageSize = pdf.GetDefaultPageSize();
                                    float pageWidth = pageSize.GetWidth();
                                    float imageHeight = 140;
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
                                document.Add(new Paragraph($"CERTIFICATE OF APPROVAL - SEA FASTENING & LASHING")
                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                    .SetFontSize(20)
                                    .SetTextAlignment(TextAlignment.CENTER));

                                document.Add(new Paragraph("\n"));


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
                                    .SetPadding(2)   // inside table cells
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetFontSize(14)
                                    .SetFont(boldFont));

                                table.AddCell(new Cell().Add(new Paragraph(model.ProjectTitle))
                                    .AddStyle(borderStyle)
                                    .SetPadding(2)   // inside table cells
                                    .SetFontSize(14)
                                        .SetTextAlignment(TextAlignment.JUSTIFIED)
                                    .SetFont(boldFont));

                                // CLIENT
                                table.AddCell(new Cell().Add(new Paragraph("CLIENT"))
                                    .AddStyle(borderStyle)
                                    .SetPadding(2)   // inside table cells

                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetFontSize(14)
                                    .SetFont(boldFont));

                                table.AddCell(new Cell().Add(new Paragraph(model.Client))
                                    .AddStyle(borderStyle)
                                    .SetPadding(2)   // inside table cells
                                    .SetFontSize(14)
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                    .SetFont(boldFont));

                                // NAME OF TOW
                                table.AddCell(new Cell().Add(new Paragraph("TRANSPORT UNIT"))
                                    .AddStyle(borderStyle)
                                    .SetPadding(2)   // inside table cells
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetFontSize(14)
                                    .SetFont(boldFont));

                                table.AddCell(new Cell().Add(new Paragraph(model.TransportUnit))
                                    .AddStyle(borderStyle)
                                    .SetFontSize(14)
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                    .SetFont(boldFont));

                                // FROM LOCATION
                                table.AddCell(new Cell().Add(new Paragraph("CARGO"))
                                    .AddStyle(borderStyle)
                                    .SetPadding(2)   // inside table cells
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)

                                    .SetFontSize(14)
                                    .SetFont(boldFont));

                                table.AddCell(new Cell().Add(new Paragraph(model.Cargo))
                                    .AddStyle(borderStyle)
                                    .SetPadding(2)   // inside table cells
                                    .SetFontSize(14)
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                    .SetFont(boldFont));

                                // TO LOCATION
                                table.AddCell(new Cell().Add(new Paragraph("FROM LOCATION"))
                                    .AddStyle(borderStyle)
                                    .SetPadding(2)   // inside table cells
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetFontSize(14)
                                    .SetFont(boldFont));

                                table.AddCell(new Cell().Add(new Paragraph(model.FromPort))
                                    .AddStyle(borderStyle)
                                    .SetPadding(2)   // inside table cells
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                    .SetFontSize(14)
                                    .SetFont(boldFont));

                                // NAME OF TUG
                                table.AddCell(new Cell().Add(new Paragraph("TO LOCATION"))
                                    .AddStyle(borderStyle)
                                    .SetPadding(2)   // inside table cells
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetFontSize(14)
                                    .SetFont(boldFont));

                                table.AddCell(new Cell().Add(new Paragraph(model.ToPort))
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                    .AddStyle(borderStyle)
                                    .SetPadding(2)   // inside table cells
                                    .SetFontSize(14)
                                    .SetFont(boldFont));


                                // Add the table to the document
                                document.Add(table);

                                document.Add(new Paragraph("\n"));

                            

                                PdfFont timesRoman = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);

                                document.Add(new Paragraph($"This is to certify that this office, acting on behalf of {model.Client}, has reviewed the procedures and witnessed the {model.CargoType} of Project cargo/equipment/machinery on the transport unit in preparation of the proposed voyage. Cargo details are annexed to this certificate. REFER ANNEXURE-2.")
                                     .SetFont(timesRoman)
                                    .SetFontSize(16)
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                     .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));
                                document.Add(new Paragraph("\n"));

                                document.Add(new Paragraph($"The {model.CargoType} are considered suitable for the proposed voyage subject to satisfactory compliance with the attached recommendations(Annexure-1).")
                                         .SetFont(timesRoman)
                                    .SetFontSize(16)
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                     .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));
                                document.Add(new Paragraph("\n"));

                                document.Add(new Paragraph($"This certificate is issued subject to {model.CertificateTitle} Terms and Conditions of Business, current at the time of issue without prejudice to any insurance interests or to any or all parties concerned.Any alterations in the surveyed items or deviations from the agreed procedures after issue of this Certificate may render the certificate invalid unless specifically approved by this office.")
                                        .SetFont(timesRoman)
                                    .SetFontSize(16)
                                    .SetTextAlignment(TextAlignment.JUSTIFIED)
                                     .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));
                                document.Add(new Paragraph("\n"));

                          


                                document.Add(new Paragraph("\n"));

                                // ---------- build the responsibility table section properly (vertical responsive layout) ----------

                                // ---------- Build the Responsibility Table (proper bottom-fixed version) ----------

                                //PdfFont boldFont = PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD);
                                PdfFont normalFont = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);

                                Table table1 = new Table(UnitValue.CreatePercentArray(new float[] { 2, 3, 4, 2, 3 }))
                                    .UseAllAvailableWidth()
                                    .SetWidth(UnitValue.CreatePercentValue(100))
                                    .SetTextAlignment(TextAlignment.LEFT)
                                    .SetHorizontalAlignment(HorizontalAlignment.LEFT)
                                    .SetFont(normalFont)
                                    .SetFontSize(10)
                                    .SetMarginTop(0)
                                     .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin)
                                    .SetMarginBottom(0);

                                // --- Header Row ---
                                string[] headers = { "Responsibility", "Name", "Location", "Signature", "Date & Time" };
                                foreach (var header in headers)
                                {
                                    table1.AddHeaderCell(new Cell()
                                        .Add(new Paragraph(header)
                                            .SetFont(boldFont)
                                            .SetFontSize(11)
                                            .SetTextAlignment(TextAlignment.LEFT))
                                        .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                         .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin)
                                        .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))

                                        .SetPadding(4));
                                }

                                // --- Issued By Row ---
                                table1.AddCell(new Cell().Add(new Paragraph("Issued By")
                                        .SetFont(boldFont)
                                        .SetFontSize(11)
                                        .SetPadding(4))
                                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                    .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                    .SetTextAlignment(TextAlignment.LEFT));

                                table1.AddCell(new Cell().Add(new Paragraph(model.IssuedBy ?? "")
                                        .SetFont(normalFont)
                                        .SetFontSize(10)
                                        .SetPadding(4))
                                    .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                    .SetTextAlignment(TextAlignment.LEFT));

                                table1.AddCell(new Cell().Add(new Paragraph(model.FromPort ?? "")
                                        .SetFont(normalFont)
                                        .SetFontSize(10)
                                        .SetPadding(4))
                                    .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                    .SetTextAlignment(TextAlignment.LEFT));

                                // --- Signature ---
                                string signaturePath1 = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model.SignaturePath?.TrimStart('/') ?? "");
                                if (System.IO.File.Exists(signaturePath1))
                                {
                                    var signatureImage = new Image(ImageDataFactory.Create(signaturePath1))
                                        .SetWidth(70)
                                        .SetHeight(35);
                                    table1.AddCell(new Cell().Add(signatureImage)
                                        .SetPadding(4)
                                        .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));
                                }
                                else
                                {
                                    table1.AddCell(new Cell().Add(new Paragraph("")).SetPadding(4).SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));
                                }

                                // --- Date ---
                                table1.AddCell(new Cell().Add(new Paragraph(
                                        model.IssuedDate.HasValue ? model.IssuedDate.Value.ToString("dd/MMM/yyyy") : "")
                                        .SetFont(normalFont)
                                        .SetFontSize(10)
                                        .SetPadding(4))
                                    .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                    .SetTextAlignment(TextAlignment.LEFT));

                                // ---------- Add Stamp + “For and on behalf of” Section ----------
                                string stampImagePath =System.IO. Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model.StampPath?.TrimStart('/') ?? "");
                                if (System.IO.File.Exists(stampImagePath))
                                {
                                    var stampImage = new Image(ImageDataFactory.Create(stampImagePath))
                                        .SetWidth(120)
                                        .SetHeight(100)
                                        .SetTextAlignment(TextAlignment.LEFT)
                                         .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin); ;

                                    document.Add(new Div().Add(stampImage).SetKeepTogether(true));
                                }

                                document.Add(new Paragraph("For and on behalf of")
                                    .SetFont(timesRoman)
                                    .SetFontSize(12)
                                    .SetMarginBottom(2)
                                    .SetKeepWithNext(true)
                                     .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));

                                document.Add(new Paragraph(model.CertificateTitle ?? "")
                                    .SetFont(boldFont)
                                    .SetFontSize(12)
                                    .SetKeepTogether(true).SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));

                                //document.Add(new Paragraph("\n"));

                                // ---------- Dynamically determine bottom placement ----------
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

                                // ---------- Fix table at the bottom of the last page ----------
                                int footerPage = pdf.GetNumberOfPages();

                                pdf.AddEventHandler(
                                    PdfDocumentEvent.END_PAGE,
                                    new BottomTableEventHandler(
                                        document,
                                        table1,
                                        new[] { footerPage },
                                        document.GetLeftMargin(),
                                        document.GetRightMargin(),
                                        40f   // distance from bottom
                                    )
                                );

                                //===================================================================================================================================================
                                // 📄 ANNEXURE-1 RECOMMENDATIONS SECTION

                                // 🔽 Reduce top margin temporarily
                                document.SetMargins(20, originalRight, originalBottom, originalLeft); // top = 20pt

                                if (model.IncludeAnnexure2)
                                {
                                    int recStartPage = pdf.GetNumberOfPages() + 1;
                                    document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));

                                    // 📌 ANNEXURE Header
                                    document.Add(new Paragraph("ANNEXURE-1 RECOMMENDATIONS TO CERTIFICATE OF APPROVAL")
                                        .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                        .SetFontSize(18)
                                        .SetTextAlignment(TextAlignment.CENTER));

                                    document.Add(new Paragraph($"{model.JobNumber}/{model.CertificateNumber}")
                                        .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                        .SetFontSize(16)
                                        .SetTextAlignment(TextAlignment.CENTER));

                                    document.Add(new Paragraph("\n\n"));

                                    // 🧾 Recommendation Table
                                    Table recTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 9 }))
                                        .UseAllAvailableWidth()
                                        .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                        .SetFontSize(12)
                                         .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin)
                                        ;

                                    recTable.AddHeaderCell(new Cell().Add(new Paragraph("SL. NO."))
                                        .SetFontSize(14)
                                        .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                        .SetTextAlignment(TextAlignment.CENTER)
                                        .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                        .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));

                                    recTable.AddHeaderCell(new Cell().Add(new Paragraph("Recommendations to be completed prior departure"))
                                        .SetFontSize(14)
                                        .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                        .SetTextAlignment(TextAlignment.CENTER)
                                        .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                        .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));

                                    if (model.Recommendations != null && model.Recommendations.Any())
                                    {
                                        for (int i = 0; i < model.Recommendations.Count; i++)
                                        {
                                            recTable.AddCell(new Cell().Add(new Paragraph((i + 1).ToString()))
                                                .SetTextAlignment(TextAlignment.CENTER)
                                                .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));

                                            recTable.AddCell(new Cell().Add(new Paragraph(model.Recommendations[i]?.RecommendationText ?? ""))
                                                .SetTextAlignment(TextAlignment.LEFT)
                                                .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                                .SetPadding(5)
                                                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN)));
                                        }
                                    }
                                    else
                                    {
                                        recTable.AddCell(new Cell().Add(new Paragraph("1"))
                                            .SetTextAlignment(TextAlignment.CENTER)
                                            .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));
                                        recTable.AddCell(new Cell().Add(new Paragraph(" "))
                                            .SetTextAlignment(TextAlignment.LEFT)
                                            .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));
                                    }

                                    // Add recommendation table
                                    document.Add(recTable);

                                    // 📦 Stamp & Signature Block (can include stamp, etc.)
                                    Div stampSignatureBlock = new Div();

                                    // 🧾 RESPONSIBILITY TABLE AT THE BOTTOM (same as main section)
                                    Table annexureFooterTable = new Table(UnitValue.CreatePercentArray(new float[] { 2, 3, 4, 2, 3 }))
                                        .UseAllAvailableWidth()
                                        .SetWidth(UnitValue.CreatePercentValue(100))
                                        .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                        .SetFontSize(10);

                                    string[] footerHeaders = { "Responsibility", "Name", "Location", "Signature", "Date & Time" };
                                    foreach (var header in footerHeaders)
                                    {
                                        annexureFooterTable.AddHeaderCell(new Cell()
                                            .Add(new Paragraph(header)
                                                .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                                .SetFontSize(11)
                                                .SetTextAlignment(TextAlignment.LEFT))
                                            .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                            .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                            .SetPadding(4)
                                             .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));
                                    }

                                    // --- Issued By Row ---
                                    annexureFooterTable.AddCell(new Cell().Add(new Paragraph("Issued By")
                                        .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                        .SetFontSize(11)
                                        .SetPadding(4))
                                        .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                        .SetTextAlignment(TextAlignment.LEFT)
                                        .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));

                                    annexureFooterTable.AddCell(new Cell().Add(new Paragraph(model.IssuedBy ?? "")
                                        .SetFontSize(10)
                                        .SetPadding(4))
                                        .SetTextAlignment(TextAlignment.LEFT)
                                        .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));

                                    annexureFooterTable.AddCell(new Cell().Add(new Paragraph(model.FromPort ?? "")
                                        .SetFontSize(10)
                                        .SetPadding(4))
                                        .SetTextAlignment(TextAlignment.LEFT)
                                        .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));

                                    string annexureSignaturePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model.SignaturePath?.TrimStart('/') ?? "");
                                    if (System.IO.File.Exists(annexureSignaturePath))

                                    {
                                        var signImg = new Image(ImageDataFactory.Create(annexureSignaturePath)).SetWidth(70).SetHeight(35);
                                        annexureFooterTable.AddCell(new Cell().Add(signImg)
                                            .SetPadding(4)
                                            .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));
                                    }
                                    else
                                    {
                                        annexureFooterTable.AddCell(new Cell().Add(new Paragraph("")).SetPadding(4).SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));
                                    }

                                    annexureFooterTable.AddCell(new Cell().Add(new Paragraph(
                                        model.IssuedDate.HasValue ? model.IssuedDate.Value.ToString("dd/MMM/yyyy") : "")
                                        .SetFontSize(10)
                                        .SetPadding(4))
                                        .SetTextAlignment(TextAlignment.LEFT)
                                        .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));

                                    // ---------- Smart page detection ----------
                                    float availableHeight1 = pdf.GetDefaultPageSize().GetHeight()
                                        - document.GetTopMargin()
                                        - document.GetBottomMargin();

                                    Rectangle measureRect1 = new Rectangle(document.GetLeftMargin(), 0,
                                        pdf.GetDefaultPageSize().GetWidth() - document.GetLeftMargin() - document.GetRightMargin(),
                                        availableHeight);

                                    var measureRenderer1 = annexureFooterTable.CreateRendererSubTree().SetParent(document.GetRenderer());
                                    var measure1= measureRenderer.Layout(new LayoutContext(new LayoutArea(pdf.GetNumberOfPages(), measureRect)));
                                    float tableHeight1 = measure.GetOccupiedArea().GetBBox().GetHeight();

                                    var currentPage1 = pdf.GetLastPage();
                                    float yPosition1 = currentPage.GetPageSize().GetHeight() - document.GetBottomMargin() - tableHeight;
                                    if (yPosition < document.GetBottomMargin() + 50)
                                    {
                                        pdf.AddNewPage();
                                    }

                                    int annexureFooterPage = pdf.GetNumberOfPages();
                                    pdf.AddEventHandler(
                                        PdfDocumentEvent.END_PAGE,
                                        new BottomTableEventHandler(
                                            document,
                                            annexureFooterTable,
                                            new[] { annexureFooterPage },
                                            document.GetLeftMargin(),
                                            document.GetRightMargin(),
                                            60f // bottom offset
                                        )
                                    );
                                }

                                //=================================================================================================================================================

                                if (model.IncludeCargoDetails && model.CargoDetails?.Any() == true)
                                {
                                    int cargoStartPage = pdf.GetNumberOfPages() + 1;
                                    document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));

                                    //📌 ANNEXURE - 2 Header Section
                                    document.Add(new Paragraph("ANNEXURE-2 CARGO DETAILS")
                                        .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                        .SetFontSize(18)
                                        .SetTextAlignment(TextAlignment.CENTER));

                                    document.Add(new Paragraph($"{model.JobNumber}/{model.CertificateNumber}")
                                        .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                        .SetFontSize(16)
                                        .SetTextAlignment(TextAlignment.CENTER));

                                    document.Add(new Paragraph("CARGO DETAILS")
                                        .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                        .SetFontSize(16)
                                        .SetTextAlignment(TextAlignment.CENTER));

                                    document.Add(new Paragraph("\n"));

                                    // ------------------ CARGO DETAILS TABLE ------------------
                                    Table cargoTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 3, 4, 3, 2, 3, 3 }))
                                        .UseAllAvailableWidth()
                                        .SetMarginTop(10)
                                         .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin);

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

                                    document.Add(new Paragraph("\n"));

                                    // 🧾 RESPONSIBILITY FOOTER TABLE AT BOTTOM (same style as before)
                                    Table cargoFooterTable = new Table(UnitValue.CreatePercentArray(new float[] { 2, 3, 4, 2, 3 }))
                                        .UseAllAvailableWidth()
                                        .SetWidth(UnitValue.CreatePercentValue(100))
                                        .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN))
                                        .SetFontSize(10);

                                    string[] footerHeaders = { "Responsibility", "Name", "Location", "Signature", "Date & Time" };
                                    foreach (var header in footerHeaders)
                                    {
                                        cargoFooterTable.AddHeaderCell(new Cell()
                                            .Add(new Paragraph(header)
                                                .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                                .SetFontSize(11)
                                                .SetTextAlignment(TextAlignment.LEFT))
                                            .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                            .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                            .SetPadding(4)
                                             .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin));
                                    }

                                    // --- Issued By Row ---
                                    cargoFooterTable.AddCell(new Cell().Add(new Paragraph("Issued By")
                                        .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                                        .SetFontSize(11)
                                        .SetPadding(4))
                                        .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                                        .SetTextAlignment(TextAlignment.LEFT)
                                        .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));

                                    cargoFooterTable.AddCell(new Cell().Add(new Paragraph(model.IssuedBy ?? "")
                                        .SetFontSize(10)
                                        .SetPadding(4))
                                        .SetTextAlignment(TextAlignment.LEFT)
                                        .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));

                                    cargoFooterTable.AddCell(new Cell().Add(new Paragraph(model.FromPort ?? "")
                                        .SetFontSize(10)
                                        .SetPadding(4))
                                        .SetTextAlignment(TextAlignment.LEFT)
                                        .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));

                                    string cargoSignaturePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model.SignaturePath?.TrimStart('/') ?? "");
                                    if (System.IO.File.Exists(cargoSignaturePath))
                                    {
                                        var signImg = new Image(ImageDataFactory.Create(cargoSignaturePath)).SetWidth(70).SetHeight(35);
                                        cargoFooterTable.AddCell(new Cell().Add(signImg)
                                            .SetPadding(4)
                                            .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));
                                    }
                                    else
                                    {
                                        cargoFooterTable.AddCell(new Cell().Add(new Paragraph("")).SetPadding(4).SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));
                                    }

                                    cargoFooterTable.AddCell(new Cell().Add(new Paragraph(
                                        model.IssuedDate.HasValue ? model.IssuedDate.Value.ToString("dd-MM-yyyy") : "")
                                        .SetFontSize(10)
                                        .SetPadding(4))
                                        .SetTextAlignment(TextAlignment.LEFT)
                                        .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));

                                    // ---------- Smart page detection ----------
                                    //float availableHeight = pdf.GetDefaultPageSize().GetHeight()
                                    //    - document.GetTopMargin()
                                    //    - document.GetBottomMargin();

                                    //Rectangle measureRect = new Rectangle(document.GetLeftMargin(), 0,
                                    //    pdf.GetDefaultPageSize().GetWidth() - document.GetLeftMargin() - document.GetRightMargin(),
                                    //    availableHeight);

                                    //var measureRenderer = cargoFooterTable.CreateRendererSubTree().SetParent(document.GetRenderer());
                                    //var measure = measureRenderer.Layout(new LayoutContext(new LayoutArea(pdf.GetNumberOfPages(), measureRect)));
                                    //float tableHeight = measure.GetOccupiedArea().GetBBox().GetHeight();

                                    //var currentPage = pdf.GetLastPage();
                                    //float yPosition = currentPage.GetPageSize().GetHeight() - document.GetBottomMargin() - tableHeight;
                                    if (yPosition < document.GetBottomMargin() + 50)
                                    {
                                        pdf.AddNewPage();
                                    }

                                    int cargoFooterPage = pdf.GetNumberOfPages();

                                    pdf.AddEventHandler(
                                        PdfDocumentEvent.END_PAGE,
                                        new BottomTableEventHandler(
                                            document,
                                            cargoFooterTable,
                                            new[] { cargoFooterPage },
                                            document.GetLeftMargin(),
                                            document.GetRightMargin(),
                                            60f // bottom offset
                                        )
                                    );
                                }

                                //=============================================================================================================================================================
                                // 📌 Add Page Break for new section
                                // 📌 Add Page Break for new section
                                // Step 3: When starting sections to skip:
                                int workDoneStart = pdf.GetNumberOfPages() + 1;
                                document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                                skipPages.Add(workDoneStart);   // ❌ skip this page
                                // --- Work Done Report page: tighter layout and pulled upward ---
                                float saveTop = document.GetTopMargin();
                                float saveRight = document.GetRightMargin();
                                float saveBot = document.GetBottomMargin();
                                float saveLeft = document.GetLeftMargin();

                                // Step 2: Load your logo
                                string imagePath1 = System.IO.Path.Combine("wwwroot/images", "Aries.png");
                                Image logo = new Image(ImageDataFactory.Create(imagePath1)).ScaleToFit(120, 100);

                                // Step 3: Create a simple header table
                                Table headerTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 4 }))
                                    .UseAllAvailableWidth();

                                headerTable.AddCell(new Cell().Add(logo)
                                    .SetBorder(Border.NO_BORDER)
                                    .SetTextAlignment(TextAlignment.LEFT)
                                    .SetVerticalAlignment(VerticalAlignment.MIDDLE));


                                headerTable.AddCell(new Cell().Add(new Paragraph("Work Done Report")
                                        .SetFont(boldFont)
                                        .SetFontSize(20)
                                        .SetTextAlignment(TextAlignment.RIGHT))
                                    .SetBorder(Border.NO_BORDER)
                                    .SetVerticalAlignment(VerticalAlignment.MIDDLE));

                                // Step 4: Add the header to the document first — acts as a pseudo "outside margin"
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
                                        $"This is certified that {model.CertificateTitle} carried out Certificate of Approval {model.CargoType} SURVEY on project {model.ProjectTitle} at {model.FromPort} as per the requirements of {model.Client}.")
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
                                        $"{model.CargoType} approval of CARGO loaded on board {model.TransportUnit} from {model.FromPort},to {model.ToPort}.")
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

                                ariesTimeCell1.Add(new Paragraph($"Name:{model.IssuedBy}")
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

                                clientTimeCell1.Add(new Paragraph($"Company:{model.Client}")
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


                                //====================================================================================================================================================================================================================================


                                int timesheetStart = pdf.GetNumberOfPages() + 1;
                                document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                                skipPages.Add(timesheetStart);  // ❌ skip Timesheet page
                                PdfFont boldFont2 = PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD);
                                PdfFont normalFont1 = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);

                                string imagePath2 = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "Aries.png");

                                // Create header table
                                Table headerTable1 = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 }))
                    .UseAllAvailableWidth();

                                // Check if image exists and add logo to left cell
                                if (System.IO.File.Exists(imagePath2))
                                {
                                    Image logo1 = new Image(ImageDataFactory.Create(imagePath2))
                                      .SetWidth(100)
                                      .SetHeight(60);

                                    headerTable1.AddCell(new Cell()
                                      .Add(logo1)
                                      .SetBorder(Border.NO_BORDER)
                                      .SetVerticalAlignment(VerticalAlignment.MIDDLE)
                                      .SetTextAlignment(TextAlignment.LEFT));
                                }
                                else
                                {
                                    // If image doesn't exist, add error placeholder in red
                                    headerTable1.AddCell(new Cell()
                      .Add(new Paragraph("" + imagePath2)
                      .SetFontColor(ColorConstants.RED))
                      .SetBorder(Border.NO_BORDER)
                      .SetVerticalAlignment(VerticalAlignment.MIDDLE));
                                }

                                // Add "Time Sheet" to right cell
                                headerTable1.AddCell(new Cell()
                    .Add(new Paragraph("Time Sheet")
                    .SetFont(timesRoman)
                      .SetFont(boldFont2)
                      .SetFontSize(20)
                      .SetTextAlignment(TextAlignment.RIGHT))
                    .SetBorder(Border.NO_BORDER)
                    .SetVerticalAlignment(VerticalAlignment.MIDDLE)
                    .SetTextAlignment(TextAlignment.RIGHT));

                                // Add header table to document
                                document.Add(headerTable1);
                                document.Add(new Paragraph("\n"));
                                // Client & Job Details Table
                                Table detailsTable = new Table(new float[] { 150f, 350f }); // Fixed width for left column
                                detailsTable.SetWidth(UnitValue.CreatePercentValue(100)).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin);
                                

                                detailsTable.AddCell(new Cell().Add(new Paragraph("CLIENT").SetFontSize(15).SetFont(boldFont)));
                                detailsTable.AddCell(new Cell().Add(new Paragraph(model.Client).SetFontSize(15).SetFont(normalFont)));

                                detailsTable.AddCell(new Cell().Add(new Paragraph("VESSEL/PROJECT NAME").SetFontSize(15).SetFont(boldFont)));
                                detailsTable.AddCell(new Cell().Add(new Paragraph(model.ProjectTitle).SetFontSize(15).SetFont(normalFont)));

                                detailsTable.AddCell(new Cell().Add(new Paragraph("LOCATION").SetFontSize(15).SetFont(boldFont)));
                                detailsTable.AddCell(new Cell().Add(new Paragraph(model.FromPort).SetFontSize(15).SetFont(normalFont)));

                                detailsTable.AddCell(new Cell().Add(new Paragraph("NAME OF PERSON").SetFontSize(15).SetFont(boldFont)));
                                detailsTable.AddCell(new Cell().Add(new Paragraph(model.IssuedBy).SetFontSize(15).SetFont(normalFont)));

                                detailsTable.AddCell(new Cell().Add(new Paragraph("TIME SHEET NO").SetFontSize(15).SetFont(boldFont)));
                                detailsTable.AddCell(new Cell().Add(new Paragraph("01").SetFontSize(15).SetFont(normalFont)));

                                detailsTable.AddCell(new Cell().Add(new Paragraph("JOB NO").SetFontSize(15).SetFont(boldFont)));
                                detailsTable.AddCell(new Cell().Add(new Paragraph(model.JobNumber).SetFontSize(15).SetFont(normalFont)));

                                detailsTable.AddCell(new Cell().Add(new Paragraph("TYPE OF JOB").SetFontSize(15).SetFont(boldFont)));
                                detailsTable.AddCell(new Cell().Add(new Paragraph("Marine warranty/Marine Assuarance Survey - Seafastening Survey").SetFontSize(15).SetFont(normalFont))); // use correct model field

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

                                ariesTimeCell.Add(new Paragraph($"Name:{model.IssuedBy}")
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

                                clientTimeCell.Add(new Paragraph($"Company:{model.Client}")
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





                                document.Close();

                                ////// Save to database
                                var pdfBytes = outputStream.ToArray();
                                _db.SeaFastenings.Add(model);
                                await _db.SaveChangesAsync();

                                var reportFolder = Path.Combine(
                                    Directory.GetCurrentDirectory(),
                                    "wwwroot",
                                    "reports",
                                    "certificates",
                                    "seafastening");
                                Directory.CreateDirectory(reportFolder);

                                var reportPath = Path.Combine(reportFolder, $"seafastening-{model.Id}.pdf");
                                await System.IO.File.WriteAllBytesAsync(reportPath, pdfBytes);
                                model.PdfFilePath = reportPath;
                                await _db.SaveChangesAsync();

                                return File(pdfBytes, "application/pdf", "Certificate.pdf");
                            }
                        }
                    }
                }


                catch (DbUpdateException dbEx)
                {
                    var inner = dbEx.InnerException?.Message ?? dbEx.Message;
                    return StatusCode(500, $"Database Error: {inner}");
                }

                catch (Exception ex)
                {
                    return StatusCode(500, $"Error generating PDF: {ex.Message}");
                }

            }
        }
    }

}
