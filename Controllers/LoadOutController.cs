using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Events;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Wmf;

using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Layout;
using iText.Layout.Properties;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Surevey_Certification_App.Controller;
using Survey_Certification_App.Data;
using Survey_Certification_App.Models;
using static iText.Svg.SvgConstants;

namespace Survey_Certification_App.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LoadOutController : Controller
    {
        private readonly ApplicationDbContext _db;

        public LoadOutController(ApplicationDbContext db)
        {
            _db = db;
        }


        [HttpPost("upload-stamp")]
        public async Task<IActionResult> UploadStamp(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

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




        [HttpPost("create-Loadout")]
        public async Task<IActionResult> CreateLoadOutCertificate([FromBody] LoadoutCertificate model)
        {

            // Failsafe fallback to prevent null or binding issues
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
                        using (var pdf = new PdfDocument(writer))
                        {
                            var skipPages = new HashSet<int>();

                            // 2️⃣ Register handler
                            var watermarkHandler = new WatermarkHandler3(skipPages);
                            pdf.AddEventHandler(PdfDocumentEvent.END_PAGE, watermarkHandler);



                            var document = new Document(pdf, PageSize.A3); // OR PageSize.A4

                            document.SetMargins(90, 50, 25, 50);


                            pdf.AddEventHandler(PdfDocumentEvent.END_PAGE, new PageBorderHandler());




                            // ✅ Load dynamic header image from model.HeaderImagePath
                            string imagePath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model.HeaderImagePath.TrimStart('/'));
                            if (System.IO.File.Exists(imagePath))
                            {
                                var pageSize = pdf.GetDefaultPageSize();
                                float pageWidth = pageSize.GetWidth();
                                float imageHeight = 140;
                                float adjustedWidth = pageWidth * 0.94f; // Reduce width to 98% (tweak this)

                                Image headerImage = new Image(ImageDataFactory.Create(imagePath))
                                    .ScaleToFit(adjustedWidth, imageHeight)
                                    .SetFixedPosition((pageWidth - adjustedWidth) / 2, pageSize.GetHeight() - imageHeight) // Center horizontally

                                    .SetMarginLeft(0).SetMarginRight(0); // Not required, but keeps things clean

                                document.Add(headerImage);
                            }
                            else
                            {
                                document.Add(new Paragraph(" ")
                                    .SetFontColor(iText.Kernel.Colors.ColorConstants.RED)
                                    .SetTextAlignment(TextAlignment.CENTER));
                            }
                            document.Add(new Paragraph("\n"));
                            document.Add(new Paragraph("\n"));

                            // ✅ Add Certificate Number And JoB Number

                            var jobPara = new Paragraph($"{model.JobNumber}/{model.CertificateNumber}")
       .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
       .SetFontSize(18)
       .SetTextAlignment(TextAlignment.RIGHT);

                            var jobDiv = new Div()
                                .SetWidth(UnitValue.CreatePercentValue(90)) // only 90% width, so moves left
                                .Add(jobPara);

                            document.Add(jobDiv);



                            // Add Certificate Title
                            document.Add(new Paragraph($"CERTIFICATE OF APPROVAL - {model.OperationType}")
                .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                .SetFontSize(20)
                .SetTextAlignment(TextAlignment.CENTER));
                            document.Add(new Paragraph("\n"));

                            // Define table with 2 columns
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


                            // Define table with 2 columns
                            var boldFont = PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD);

                            // Create a table with 2 columns (left fixed width)
                            Table table = new Table(UnitValue.CreatePercentArray(new float[] { 30, 70 })) // 30% left, 70% right
                                .UseAllAvailableWidth()
                .SetFixedLayout()
                  .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin); ;

                            // Set border style for each cell for visual clarity
                            Style borderStyle = new Style()
                .SetBorder(new SolidBorder(ColorConstants.BLACK, 1));
                            // Add project details as key-value pairs
                            table.AddCell(new Cell().Add(new Paragraph("PROJECT"))
                .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                .SetPadding(2)   // inside table cells
                                .SetFontSize(14)
                .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD)));

                            table.AddCell(new Cell().Add(new Paragraph(model.ProjectTitle))
                              .SetFontSize(14)
                                .SetPadding(2)   // inside table cells
                                              .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD)));

                            // Client
                            table.AddCell(new Cell().Add(new Paragraph("CLIENT"))
                .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                .SetFontSize(14)
                  .SetPadding(2)   // inside table cells
                                .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD)));

                            table.AddCell(new Cell().Add(new Paragraph(model.Client))
                              .SetFontSize(14)
                              .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD)));

                            // Tow Name
                            table.AddCell(new Cell().Add(new Paragraph("CARGO / MODULE"))
                .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                  .SetPadding(2)   // inside table cells
                                .SetFontSize(14)
                .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD)));

                            table.AddCell(new Cell().Add(new Paragraph(model.Cargo))
                                .SetPadding(2)   // inside table cells
                                              .SetFontSize(14)
                              .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD)));

                            // FROM
                            table.AddCell(new Cell().Add(new Paragraph("ONTO BARGE/VESSEL"))
                  .SetPadding(2)   // inside table cells
                                .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                .SetFontSize(14)
                .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD)));

                            table.AddCell(new Cell().Add(new Paragraph(model.BargeVessel))
                              .SetFontSize(14)
                                .SetPadding(2)   // inside table cells
                                              .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD)));

                            // TO
                            table.AddCell(new Cell().Add(new Paragraph("AT LOCATION"))
                .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                  .SetPadding(2)   // inside table cells
                                .SetFontSize(14)
                .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD)));

                            table.AddCell(new Cell().Add(new Paragraph(model.Location))
                                .SetPadding(2)   // inside table cells
                                              .SetFontSize(14)
                              .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD)));


                            // Tug Name
                            table.AddCell(new Cell().Add(new Paragraph("TYPE OF OPERATION"))
               .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                 .SetPadding(2)   // inside table cells
                               .SetFontSize(14)
               .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD)));

                            table.AddCell(new Cell().Add(new Paragraph(model.OperationType))
                                .SetPadding(2)   // inside table cells
                                              .SetFontSize(14)
                              .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD)));

                            // Tug Name
                            table.AddCell(new Cell().Add(new Paragraph("MODE OF OPERATION"))
               .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                 .SetPadding(2)   // inside table cells
                               .SetFontSize(14)
               .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD)));

                            table.AddCell(new Cell().Add(new Paragraph(model.Modeofoperation))
                                .SetPadding(2)   // inside table cells
                                              .SetFontSize(14)
                              .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD)));


                            // Add the table to the document
                            document.Add(table);
                            document.Add(new Paragraph("\n"));

                            PdfFont timesRoman = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);

                            document.Add(new Paragraph($"This is to certify that this office, acting on behalf of {model.Client},has reviewed relevant drawings and calculations and witnessed the preparations for the above mentioned {model.OperationType}.")
                                 .SetFont(timesRoman)
                                .SetFontSize(16)
                                .SetTextAlignment(TextAlignment.JUSTIFIED)
                                 .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                .SetMarginRight(innerMargin));
                            document.Add(new Paragraph("\n"));

                            document.Add(new Paragraph($"This operation is hereby approved to proceed upon receipt of a good weather forecast and favourable outlook.")
                                     .SetFont(timesRoman)
                                .SetFontSize(16)
                                .SetTextAlignment(TextAlignment.JUSTIFIED)
                                 .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                .SetMarginRight(innerMargin));
                            document.Add(new Paragraph("\n"));

                            document.Add(new Paragraph($"This certificate is issued subject to {model.CertificateTitle} Terms and Conditions of Business,current at the time of issue without prejudice to any insurance interests or to any or all parties concerned. Any alterations in the surveyed items or deviations from the agreed procedures after issue of this certificate may render the Certificate invalid unless specifically approved by this office.")
                                    .SetFont(timesRoman)
                                .SetFontSize(16)
                                .SetTextAlignment(TextAlignment.JUSTIFIED)
                                 .SetMarginBottom(2).SetMarginLeft(innerMargin)
                                .SetMarginRight(innerMargin));
                            document.Add(new Paragraph("\n"));




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

                            table1.AddCell(new Cell().Add(new Paragraph(model.Location ?? "")
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
                                    model.IssuedDate.HasValue ? model.IssuedDate.Value.ToString("dd/MM/yyyy") : "")
                                    .SetFont(normalFont)
                                    .SetFontSize(10)
                                    .SetPadding(4))
                                .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f))
                                .SetTextAlignment(TextAlignment.LEFT));

                            // ---------- Add Stamp + “For and on behalf of” Section ----------
                            string stampImagePath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model.StampPath?.TrimStart('/') ?? "");
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

                            //=================================================================================================================================================


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
                                    .Add(new Paragraph(model.IssuedBy ?? "")
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
                                        model.IssuedDate.HasValue ? model.IssuedDate.Value.ToString("dd/MM/yyyy") : "")
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



                            //=================================================================================================================================================
                            int workDonePage = pdf.GetNumberOfPages() + 1;
                            document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                            skipPages.Add(workDonePage);   // ❌ skip watermark here
                            /// Load the image
                            string imagePath1 = "wwwroot/images/Aries.png"; // Save your image in wwwroot
                            Image logo = new Image(ImageDataFactory.Create(imagePath1)).SetWidth(100).SetHeight(60);
                            PdfFont boldFont7 = PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD);
                            // Header Table
                            Table headerTable = new Table(2).UseAllAvailableWidth();
                            headerTable.AddCell(new Cell().Add(logo).SetBorder(Border.NO_BORDER).SetVerticalAlignment(VerticalAlignment.MIDDLE));
                            headerTable.AddCell(new Cell().Add(new Paragraph("Work Done Report")
                              .SetFont(PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD))
                              .SetFontSize(20)
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
                                    $"This is certified that {model.CertificateTitle} carried out certificate of Marine warranty survey for {model.OperationType} on project {model.ProjectTitle} at {model.Location} as per the requirements of {model.Client}.")
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
                                    $"Loadout approval for loadout of {model.Cargo} onto {model.BargeVessel} at {model.Location}.")
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
                            //document.SetMargins(saveTop, saveRight, saveBot, saveLeft);



                            //=============================================================================================================================================================

                            int timesheetPage = pdf.GetNumberOfPages() + 1;
                            document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                            skipPages.Add(timesheetPage);  // ❌ skip watermark here

                            PdfFont boldFont2 = PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD);
                            //PdfFont normalFont1 = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);

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
                  .SetFont(boldFont)
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
                            detailsTable.SetWidth(UnitValue.CreatePercentValue(100));
                                    detailsTable.SetWidth(UnitValue.CreatePercentValue(100)).SetMarginLeft(innerMargin)
                                    .SetMarginRight(innerMargin);

                            detailsTable.AddCell(new Cell().Add(new Paragraph("CLIENT").SetFontSize(15).SetFont(boldFont)));
                            detailsTable.AddCell(new Cell().Add(new Paragraph(model.Client).SetFontSize(15).SetFont(normalFont)));

                            detailsTable.AddCell(new Cell().Add(new Paragraph("VESSEL/PROJECT NAME").SetFontSize(15).SetFont(boldFont)));
                            detailsTable.AddCell(new Cell().Add(new Paragraph(model.ProjectTitle).SetFontSize(15).SetFont(normalFont)));

                            detailsTable.AddCell(new Cell().Add(new Paragraph("LOCATION").SetFontSize(15).SetFont(boldFont)));
                            detailsTable.AddCell(new Cell().Add(new Paragraph(model.Location).SetFontSize(15).SetFont(normalFont)));

                            detailsTable.AddCell(new Cell().Add(new Paragraph("NAME OF PERSON").SetFontSize(15).SetFont(boldFont)));
                            detailsTable.AddCell(new Cell().Add(new Paragraph(model.Client).SetFontSize(15).SetFont(normalFont)));

                            detailsTable.AddCell(new Cell().Add(new Paragraph("TIMESHEET NO").SetFontSize(15).SetFont(boldFont)));
                            detailsTable.AddCell(new Cell().Add(new Paragraph("01").SetFontSize(15).SetFont(normalFont)));

                            detailsTable.AddCell(new Cell().Add(new Paragraph("JOB NO").SetFontSize(15).SetFont(boldFont)));
                            detailsTable.AddCell(new Cell().Add(new Paragraph(model.JobNumber).SetFontSize(15).SetFont(normalFont)));

                            detailsTable.AddCell(new Cell().Add(new Paragraph("TYPE OF JOB").SetFontSize(15).SetFont(boldFont)));
                            detailsTable.AddCell(new Cell().Add(new Paragraph($"MWS service for : {model.OperationType}").SetFontSize(15).SetFont(normalFont))); // use correct model field

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
                            document.Add(timesheetTable);



                            int lastPage = pdf.GetNumberOfPages(); // or your custom target page
                            pdf.AddEventHandler(PdfDocumentEvent.END_PAGE, new CustomFooterHandler(new[] { 2, lastPage }));


                            //======================================================================================================
                            // Add Page Break before checklist
                            document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                            PdfFont boldFont3 = PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD);
                            PdfFont regularFont = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);

                            // Outer table for 2-column layout (Title and Survey Type)
                            Table headerTable2 = new Table(UnitValue.CreatePercentArray(new float[] { 70, 30 }))
                .UseAllAvailableWidth();

                            // Row 1: ARIES MARINE SITE CHECKLIST | LOAD-OUT SURVEY
                            Cell leftTitleCell = new Cell()
                .Add(new Paragraph("ARIES SITE CHECKLIST")
                .SetFont(boldFont)
                .SetFontSize(15))
                .SetTextAlignment(TextAlignment.LEFT);
                            headerTable2.AddCell(leftTitleCell);

                            Cell rightSurveyCell = new Cell()
                              .Add(new Paragraph($"{model.OperationType} SURVEY")
                              .SetFont(boldFont2)
                              .SetFontSize(15))
                              .SetTextAlignment(TextAlignment.LEFT);
                            headerTable2.AddCell(rightSurveyCell);

                            // Row 2: PROJECT NAME | CoA No
                            Cell leftProjectCell = new Cell()
                .Add(new Paragraph($"PROJECT NAME :{model.ProjectTitle}")
                .SetFont(boldFont2)
                .SetFontSize(15))
                .SetTextAlignment(TextAlignment.LEFT);
                            headerTable2.AddCell(leftProjectCell);

                            Cell rightCoACell = new Cell()
                              .Add(new Paragraph($"CoA No: {model.JobNumber} / {model.CertificateNumber}")
                              .SetFont(regularFont)
                              .SetFontSize(15))
                              .SetTextAlignment(TextAlignment.LEFT);
                            headerTable2.AddCell(rightCoACell);

                            // Row 3: Document
                            headerTable2.AddCell(new Cell(1, 2)  // spans both columns
                                .Add(new Paragraph("Document")
                  .SetFont(regularFont)
                  .SetFontSize(15))
                .SetTextAlignment(TextAlignment.LEFT));

                            Table headerTable9 = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 })).UseAllAvailableWidth();
                            headerTable9.AddCell(new Cell().Add(new Paragraph($"").SetFont(boldFont3)).SetBorder(Border.NO_BORDER));
                            headerTable9.AddCell(new Cell().Add(new Paragraph(model.CertificateTitle).SetFont(boldFont3).SetTextAlignment(TextAlignment.RIGHT)).SetBorder(Border.NO_BORDER));
                            document.Add(headerTable9);


                            // Add the table to the document
                            document.Add(headerTable2);


                            PdfFont boldFont4 = PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD);
                            PdfFont regularFont2 = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);

                            // Create the table with two columns
                            Table checklistTable = new Table(UnitValue.CreatePercentArray(new float[] { 30, 70 }))
              .UseAllAvailableWidth()
              .SetMarginTop(8);

                            // Function to add a section
                            void AddChecklistSection(string sectionTitle, List<string> items)
                            {
                                // Add the section title once
                                checklistTable.AddCell(new Cell(items.Count, 1)
                .Add(new Paragraph(sectionTitle)
                  .SetFont(boldFont)
                  .SetFontSize(10))
                .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                .SetTextAlignment(TextAlignment.LEFT)
                .SetVerticalAlignment(VerticalAlignment.TOP)
                .SetPadding(5)
                .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));

                                // Add each item in the right column
                                foreach (string item in items)
                                {
                                    checklistTable.AddCell(new Cell()
                                      .Add(new Paragraph(item)
                                        .SetFont(regularFont)
                                        .SetFontSize(9))
                                      .SetPadding(6)
                                      .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.5f)));



                                }
                            }

                            // Now add your sections
                            AddChecklistSection("General comments:", new List<string> {
    "All outstanding comments from documentation reviews, meetings, HAZOPs, etc. closed out.",
    "Ensure all safety arrangements made.",
    "Is all necessary site information available?",
    "Are time schedules realistic?",
    "Establish reporting and recording procedures with Client.",
    "Ensure step-by-step operational procedures are available."
  });

                            AddChecklistSection("Structure:", new List<string> {
    "Weight within allowable limits.",
    "CoG within allowable limits; Scaffolding, attachments, umbilicals etc. disconnected.",
    "Loose objects secured or removed.",
    "Checks made for clashes and protruding objects both under and to sides of structure.",
    "Sea fastenings ready for immediate use after."
  });

                            AddChecklistSection("Grillage:", new List<string> {
    "Grillage in place on barge/vessel or structure.",
    "Grillage correctly located.",
    "As built and NDE reports acceptable.",
    "Shimming available for set down.",
    "Check welding complete."
  });

                            AddChecklistSection("Barge/Vessel:", new List<string> {
    "Suitability survey performed and any applicable comments or recommendations closed out.",
    "Measuring systems set up for draft, heel, trim, ballast, etc. during operations.",
    "Check vessel attachments: padeyes, mooring points, winch foundations etc.",
    "Procedures for barge movements before, during and after loadout established."
  });

                            AddChecklistSection("Ballasting:", new List<string> {
    "Barge ballast/de-ballast plan satisfactory and according to calculations.",
    "Vessel ballast system in working order.",
    "Portable ballast pumps and power packs in place and function tested.",
    "Reserve equipment available at convenient locations.",
    "Is level of pumping redundancy adequate?",
    "Vessel correctly trimmed and ballasted."
  });



                            AddChecklistSection("Mooring:", new List<string> {
    "Quay strength and suitability checked.",
    "Moorings system suitable.",
    "Tugs/assisting vessels present.",
    "Barge moorings checked, tensioned and satisfactory."
  });

                            AddChecklistSection("Loadout Method:", new List<string> {
    "GENERAL:",
    "Loadout meeting to be held.",
    "Establish method of retrieval.",
    "Establish contingency plans and equipment in suitable state of readiness.",
    "Back-up power systems functioning correctly.",
    "Loadout path clear.",
    "Loadout path for trailers marked.",
    "Stoppers fitted at end or trailer route and locating points also in place.",
    "Loadout area roped off.",
    "Certification for loadout equipment satisfactory.",
    " Organisation and manpower satisfactory.",
    " Method of communications established and checked.",
    "Adequate lighting.",











  });
                            AddChecklistSection("", new List<string> {
                "SKIDDED LOADOUT",
                 " Skid beams all in place and adequately supported",
                   " Skidways greased.",
                    "Winches/jacks tested running and maintenance checked.",
                      "Winch diesel and hydraulic reservoir tanks full.",
                      "Winch brake and clutch controls functioning.",
                       "Manholes, cable ducts all plated over.",
                       "Link beam connections fitted.",
                        "All reeving, shackles and rigging correctly fitted.",
              });

                            AddChecklistSection("", new List<string> {
                             "TRAILER LOADOUT",
                  " Maintenance schedules checked",
                  " Lubricant and hydraulic levels checked.",
                  " Tyres checked.",
                  " Suspension, steering, propulsion and braking systems.",
                  " Hydraulic pressures under load within permitted levels.",
                  " Integrity of all trailer packings checked.",
                   " Ramp in place.",
                   " Trailers all functioning correctly and running.",
                   "Load installed correctly.",
                   " Load connections to trailer fitted.",

  });


                            AddChecklistSection("", new List<string> {
                             " LIFTED LOADOUT",
                             " Rigging examined and satisfactory.",
                             " All crane certificates in order.",
                             " All rigging certificates in order.",
                             " Visual crane inspection satisfactory."


              });



                            AddChecklistSection(" Environmental:", new List<string> {
      "Weather conditions, forecast and window acceptable.",
  "Water depth and tidal conditions acceptable.",
  "Tide measuring equipment set up and tested.",
  "River movements cancelled.",
  "Underkeel clearance satisfactory."
  });
                            AddChecklistSection("COA:", new List<string> {
"Certificate of Approval issued for loadout."
  });

                            AddChecklistSection("Post Loadout:", new List<string> {
"Minimum seafastening for turn of barge / vessel installed.",
"Assisting tugs / vessels for turn of barge / vessel present and connected  properly.",

" Barge moored safely for period, location, season."
  });

                            // Add table to document
                            document.Add(checklistTable);

                            // Spacer to push footer to the bottom
                            // Create Helvetica font
                            PdfFont helvetica = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);

                            // Spacer to push to bottom of page (manual, adjust as needed)
                            document.Add(new Paragraph("\n\n\n\n\n\n\n\n\n"));


                            //PdfFont timesBold = PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD);
                            PdfFont timesRegular = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);


                            // One-row, two-column table aligned side-by-side
                            Table signTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 }))
                .UseAllAvailableWidth();
                            // Checked By (left)
                            signTable.AddCell(new Cell().Add(new Paragraph()
                  .Add(new Text("Checked By: ").SetFont(timesBold).SetFontSize(12))
                  .Add(new Text(" ______________________________").SetFont(timesRegular).SetFontSize(10)))
                .SetBorder(Border.NO_BORDER)
                .SetTextAlignment(TextAlignment.LEFT));

                            // Date (right)
                            signTable.AddCell(new Cell().Add(new Paragraph()
                  .Add(new Text("Date: ").SetFont(timesBold).SetFontSize(12))
                  .Add(new Text(" ______________________________").SetFont(timesRegular).SetFontSize(10)))
                .SetBorder(Border.NO_BORDER)
                .SetTextAlignment(TextAlignment.RIGHT));

                            // Add to document
                            document.Add(signTable);

                            //======================================================================================================
                            document.Close();

                            ////// Save to database
                            var pdfBytes = outputStream.ToArray();
                            _db.loadoutCertificates.Add(model);
                            await _db.SaveChangesAsync();

                            var reportFolder = System.IO.Path.Combine(
                                Directory.GetCurrentDirectory(),
                                "wwwroot",
                                "reports",
                                "certificates",
                                "loadout");
                            Directory.CreateDirectory(reportFolder);

                            var reportPath = System.IO.Path.Combine(reportFolder, $"loadout-{model.Id}.pdf");
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
