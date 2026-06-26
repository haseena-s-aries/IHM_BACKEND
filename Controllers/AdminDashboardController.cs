using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Survey_Certification_App.Data;
using Survey_Certification_App.DTOS;
using Survey_Certification_App.Models;
using Survey_Certification_App.Models.Survey;

namespace Survey_Certification_App.Controllers
{
    [Route("api/[controller]")]

    [ApiController]
    [Authorize]
    public class AdminDashboardController : Controller
    {
        private readonly ApplicationDbContext _db;

        public AdminDashboardController(ApplicationDbContext db)
        {
            _db = db;
        }



        [HttpGet("GetAll")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _db.users.ToListAsync();
            return Ok(users);
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("create-certificate-with-pdf")]
        public async Task<IActionResult> CreateCertificateWithPdf(
    [FromForm] string certificateType,
    [FromForm] string certificateData,
    [FromForm] IFormFile pdfFile)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(certificateType))
                    return BadRequest("Certificate type is required");

                if (string.IsNullOrWhiteSpace(certificateData))
                    return BadRequest("Certificate data is required");

                if (pdfFile == null)
                    return BadRequest("PDF file is required");

                var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedPDFs");
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                var fileName = $"{certificateType}_{DateTime.Now:yyyyMMddHHmmss}_{pdfFile.FileName}";
                var filePath = Path.Combine(folderPath, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await pdfFile.CopyToAsync(stream);
                }

                switch (certificateType.ToLower())
                {
                    case "towage":
                        var towageDto = JsonConvert.DeserializeObject<Certificate>(certificateData);
                        if (towageDto == null) return BadRequest("Invalid Towage JSON");
                        towageDto.PdfFilePath = filePath;
                        _db.certificates.Add(towageDto);
                        break;

                    case "seafastening":
                        var sfDto = JsonConvert.DeserializeObject<SeaFastening>(certificateData);
                        if (sfDto == null) return BadRequest("Invalid SeaFastening JSON");
                        sfDto.PdfFilePath = filePath;
                        _db.SeaFastenings.Add(sfDto);
                        break;

                    case "loadout":
                        var loadDto = JsonConvert.DeserializeObject<LoadoutCertificate>(certificateData);
                        if (loadDto == null) return BadRequest("Invalid Loadout JSON");
                        loadDto.PdfFilePath = filePath;
                        _db.loadoutCertificates.Add(loadDto);
                        break;

                    default:
                        return BadRequest("Invalid certificate type.");
                }

                await _db.SaveChangesAsync();
                return Ok(new { message = "Certificate & PDF uploaded successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message} - {ex.StackTrace}");
            }
        }

        [HttpGet("view-pdf/{type}/{id}")]
        public async Task<IActionResult> ViewPdf(string type, int id)
        {
            object cert = null;

            switch (type.ToLower())
            {
                case "towage":
                    cert = await _db.certificates.FindAsync(id);
                    break;
                case "seafastening":
                    cert = await _db.SeaFastenings.FindAsync(id);
                    break;
                case "loadout":
                    cert = await _db.loadoutCertificates.FindAsync(id);
                    break;
                default:
                    return BadRequest("Invalid certificate type.");
            }

            if (cert == null)
                return NotFound("Certificate not found.");

            var pdfPath = (string)cert.GetType().GetProperty("PdfFilePath")?.GetValue(cert);
            if (string.IsNullOrEmpty(pdfPath) || !System.IO.File.Exists(pdfPath))
                return NotFound("PDF file not found.");

            var pdfBytes = await System.IO.File.ReadAllBytesAsync(pdfPath);
            return File(pdfBytes, "application/pdf", Path.GetFileName(pdfPath));
        }







        [HttpGet("GetById/{id}")]
        public async Task<IActionResult> GetUserById(int id)
        {
            var user = await _db.users.FindAsync(id);
            if (user == null) return NotFound("User not found");
            return Ok(user);
        }



        [HttpPut("Save")]
        public async Task<IActionResult> SaveUser([FromBody] UserDto userDto)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors)
                                              .Select(e => e.ErrorMessage);
                return BadRequest(new { message = "Validation failed", errors });
            }

            var user = await _db.users.FindAsync(userDto.Id);
            if (user == null)
                return NotFound("User not found");

            var email = userDto.Email?.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(email))
                return BadRequest(new { message = "Email is required" });

            var emailExists = await _db.users.AnyAsync(u =>
                u.Id != userDto.Id &&
                u.Email.ToLower() == email);

            if (emailExists)
                return BadRequest(new { message = "Email already exists" });

            user.FullName = userDto.FullName;
            user.Email = email;
            user.Role = userDto.Role;


            await _db.SaveChangesAsync();

            return Ok(new { message = "User updated successfully" });
        }

        [HttpPost("Save")]
        public async Task<IActionResult> CreateUser([FromBody] UserDto userDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var email = userDto.Email?.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(email))
                return BadRequest(new { message = "Email is required" });

            var emailExists = await _db.users.AnyAsync(u => u.Email.ToLower() == email);
            if (emailExists)
                return BadRequest(new { message = "Email already exists" });

            var user = new User
            {
                FullName = userDto.FullName,
                Email = userDto.Email.ToLower(),
                Role = userDto.Role,
                IsApproved = true,
                RequestedRole = null,
                IsOtpVerified = true,
                ApprovedAt = DateTime.UtcNow,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(userDto.Password)
            };

            _db.users.Add(user);
            await _db.SaveChangesAsync();

            return Ok(new { message = "User created successfully" });
        }

        [HttpDelete("Delete/{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _db.users.FindAsync(id);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            _db.users.Remove(user);
            await _db.SaveChangesAsync();
            return Ok("User deleted successfully.");
        }



        [HttpGet("certificate-report")]
        public async Task<IActionResult> GetCertificateReport()
        {
            int towageCount = await _db.certificates.CountAsync();
            int seaFasteningCount = await _db.SeaFastenings.CountAsync();
            int loadoutCount = await _db.loadoutCertificates.CountAsync();

            var dto = new TotalCertificateCountDto
            {
                TowageCount = towageCount,
                SeaFasteningCount = seaFasteningCount,
                LoadoutCount = loadoutCount,
                TotalCertificates = towageCount + seaFasteningCount + loadoutCount
            };

            return Ok(dto);
        }

        [HttpGet("report-data/{type}/{id}")]
        public async Task<IActionResult> GetReportData(string type, int id)
        {
            object? report = type.Trim().ToLowerInvariant() switch
            {
                "towage" => await _db.certificates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id),
                "seafastening" => await _db.SeaFastenings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id),
                "loadout" => await _db.loadoutCertificates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id),
                _ => null
            };

            return report == null ? NotFound() : Ok(report);
        }

        [HttpGet("saved-reports")]
        public async Task<IActionResult> GetSavedReports()
        {
            var towage = await _db.certificates.AsNoTracking().Select(x => new SavedReportDto
            {
                Id = x.Id,
                ReportType = "towage",
                Title = string.IsNullOrWhiteSpace(x.ProjectTitle) ? x.CertificateType : x.ProjectTitle,
                VesselName = string.IsNullOrWhiteSpace(x.TowName) ? x.TugName : x.TowName,
                Reference = x.CertificateNumber,
                SavedDate = x.IssuedDate,
                PdfFilePath = x.PdfFilePath
            }).ToListAsync();

            var seaFastening = await _db.SeaFastenings.AsNoTracking().Select(x => new SavedReportDto
            {
                Id = x.Id,
                ReportType = "seafastening",
                Title = string.IsNullOrWhiteSpace(x.ProjectTitle) ? x.CertificateTitle : x.ProjectTitle,
                VesselName = x.TransportUnit,
                Reference = x.CertificateNumber,
                SavedDate = x.IssuedDate,
                PdfFilePath = x.PdfFilePath
            }).ToListAsync();

            var loadout = await _db.loadoutCertificates.AsNoTracking().Select(x => new SavedReportDto
            {
                Id = x.Id,
                ReportType = "loadout",
                Title = string.IsNullOrWhiteSpace(x.ProjectTitle) ? x.CertificateTitle : x.ProjectTitle,
                VesselName = x.BargeVessel,
                Reference = x.CertificateNumber,
                SavedDate = x.IssuedDate,
                PdfFilePath = x.PdfFilePath
            }).ToListAsync();

            var ihmJobs = await _db.SurveyJobs
                .AsNoTracking()
                .Where(x => x.ModuleType == SurveyModuleType.IhmSurvey)
                .Select(x => new { x.Id, x.VesselName, x.ProjectName, x.JobNumber })
                .ToListAsync();

            var reportsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "reports", "ihm");
            var ihmReports = ihmJobs.Select(job => new
                {
                    Job = job,
                    PdfPath = Path.Combine(reportsRoot, job.Id.ToString(), "ihm-report.pdf")
                })
                .Where(x => System.IO.File.Exists(x.PdfPath))
                .Select(x => new SavedReportDto
                {
                    Id = x.Job.Id,
                    ReportType = "ihm",
                    Title = string.IsNullOrWhiteSpace(x.Job.ProjectName) ? "IHM Survey Report" : x.Job.ProjectName,
                    VesselName = x.Job.VesselName,
                    Reference = x.Job.JobNumber,
                    SavedAt = System.IO.File.GetLastWriteTimeUtc(x.PdfPath),
                    PdfFilePath = x.PdfPath
                });

            return Ok(towage
                .Concat(seaFastening)
                .Concat(loadout)
                .Concat(ihmReports)
                .OrderByDescending(x => x.SortDate)
                .ThenByDescending(x => x.Id)
                .Take(50)
                .ToList());
        }

        public sealed class SavedReportDto
        {
            public int Id { get; set; }
            public string ReportType { get; set; } = string.Empty;
            public string? Title { get; set; }
            public string? VesselName { get; set; }
            public string? Reference { get; set; }
            public DateOnly? SavedDate { get; set; }
            public DateTime? SavedAt { get; set; }
            [System.Text.Json.Serialization.JsonIgnore]
            public string? PdfFilePath { get; set; }
            public DateTime SortDate => SavedAt
                ?? (SavedDate.HasValue
                    ? SavedDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                    : DateTime.MinValue);
        }



        // ---------- CERTIFICATE LISTS BY TYPE ----------
        [HttpGet("GetCertificates")]
        public async Task<IActionResult> GetCertificates(string search = "", string type = "", int page = 1, int pageSize = 10)
        {
            if (string.IsNullOrEmpty(type))
                return BadRequest("Type is required");

            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 10;

            IQueryable<object> query;

            if (type.ToLower() == "seafastening")
            {
                var data = _db.SeaFastenings.AsQueryable();

                if (!string.IsNullOrEmpty(search))
                {
                    data = data.Where(c =>
                        c.CertificateNumber.Contains(search) ||
                        c.Client.Contains(search) ||
                        c.JobNumber.Contains(search));
                }

                var totalCount = await data.CountAsync();
                var result = await data
                    .Select(c => new cerificatedto
                    {
                        Id = c.Id,
                        CertificateNumber = c.CertificateNumber,
                        ProjectTitle = c.ProjectTitle,
                        ClientName = c.Client,
                        JobNumber = c.JobNumber
                    })
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                .ToListAsync();

                return Ok(new PagedResult<cerificatedto> { Data = result, TotalCount = totalCount });
            }
            else if (type.ToLower() == "towage")
            {
                var data = _db.certificates.AsQueryable();

                if (!string.IsNullOrEmpty(search))
                {
                    data = data.Where(c =>
                        c.CertificateNumber.Contains(search) ||
                        c.ClientName.Contains(search) ||
                        c.JobNumber.Contains(search));
                }

                var totalCount = await data.CountAsync();
                var result = await data
                    .Select(c => new cerificatedto
                    {
                        Id = c.Id,
                        CertificateNumber = c.CertificateNumber,
                        ProjectTitle = c.ProjectTitle,
                        ClientName = c.ClientName,
                        JobNumber = c.JobNumber
                    })
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                .ToListAsync();

                return Ok(new PagedResult<cerificatedto> { Data = result, TotalCount = totalCount });
            }
            else if (type.ToLower() == "loadout")
            {
                var data = _db.loadoutCertificates.AsQueryable();

                if (!string.IsNullOrEmpty(search))
                {
                    data = data.Where(c =>
                        c.CertificateNumber.Contains(search) ||
                        c.Client.Contains(search) ||
                        c.JobNumber.Contains(search));
                }

                var totalCount = await data.CountAsync();
                var result = await data
                    .Select(c => new cerificatedto
                    {
                        Id = c.Id,
                        CertificateNumber = c.CertificateNumber,
                        ProjectTitle = c.ProjectTitle,
                        ClientName = c.Client,
                        JobNumber = c.JobNumber
                    })
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                .ToListAsync();

                return Ok(new PagedResult<cerificatedto> { Data = result, TotalCount = totalCount });
            }

            return BadRequest("Invalid type selected.");
        }


        // ---------- GET BY ID ----------

        [HttpGet("GetSeaFasteningById/{id}")]
        public async Task<IActionResult> GetSeaFasteningById(int id)
        {
            try
            {
                var cert = await _db.SeaFastenings.FindAsync(id);
                if (cert == null)
                    return NotFound("Sea Fastening Certificate not found");

                return Ok(cert);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Exception: {ex.Message}");
            }
        }

        [HttpGet("GetTowageById/{id}")]
        public async Task<IActionResult> GetTowageById(int id)
        {
            try
            {
                var cert = await _db.certificates.FindAsync(id);
                if (cert == null)
                    return NotFound("Towage Certificate not found");
                return Ok(cert);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Exception:{ex.Message}");
            }
        }

        [HttpGet("GetLoadoutById/{id}")]
        public async Task<IActionResult> GetLoadoutById(int id)
        {
            try
            {
                var cert = await _db.loadoutCertificates.FindAsync(id);
                if (cert == null)
                    return NotFound("Loadout Certificate not found");
                return Ok(cert);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Exception:{ex.Message}");
            }
        }



        [HttpGet("generate-pdf")]
        public async Task<IActionResult> GeneratePdf(string type, int id)
        {
            if (string.IsNullOrWhiteSpace(type))
                return BadRequest("Certificate type is required");

            object cert = null;

            switch (type.ToLower())
            {
                case "seafastening":
                    cert = await _db.SeaFastenings.FindAsync(id);
                    break;
                case "towage":
                    cert = await _db.certificates.FindAsync(id);
                    break;
                case "loadout":
                    cert = await _db.loadoutCertificates.FindAsync(id);
                    break;
                default:
                    return BadRequest("Invalid certificate type.");
            }

            if (cert == null)
                return NotFound("Certificate not found.");

            // ✅ Call the helper function inside the same class
            byte[] pdfBytes = GenerateCertificatePdf(cert, type);

            Response.Headers.Add("Content-Disposition", "inline; filename=" + $"{type}_certificate_{id}.pdf");
            return File(pdfBytes, "application/pdf");

        }

        // ✅ PLACE THIS HELPER METHOD INSIDE THIS CLASS
        private byte[] GenerateCertificatePdf(object cert, string type)
        {
            using (var ms = new MemoryStream())
            {
                var writer = new iText.Kernel.Pdf.PdfWriter(ms);
                var pdf = new iText.Kernel.Pdf.PdfDocument(writer);
                var document = new iText.Layout.Document(pdf, iText.Kernel.Geom.PageSize.A4);
                document.SetMargins(40, 40, 40, 40);

                // Fonts and styles
                var bold = iText.Layout.Properties.TextAlignment.LEFT;
                var titleFont = iText.Kernel.Font.PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLD);
                var normalFont = iText.Kernel.Font.PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);

                // Title
                document.Add(new iText.Layout.Element.Paragraph("CERTIFICATE REPORT")
                    .SetFont(titleFont)
                    .SetFontSize(16)
                    .SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER)
                    .SetMarginBottom(20));

                // Helper: Add a labeled field
                void AddField(string label, string value)
                {
                    var p = new iText.Layout.Element.Paragraph()
                        .Add(new iText.Layout.Element.Text($"{label}: ").SetFont(titleFont))
                        .Add(new iText.Layout.Element.Text(value ?? "").SetFont(normalFont));
                    document.Add(p);
                }

                // Populate by type
                switch (type.ToLower())
                {
                    case "seafastening":
                        var sf = cert as SeaFastening;
                        AddField("CERTIFICATE NO", sf.CertificateNumber);
                        AddField("JOB NO", sf.JobNumber);
                        AddField("PROJECT", sf.ProjectTitle);
                        AddField("CLIENT", sf.Client);
                        AddField("TRANSPORT UNIT", sf.TransportUnit);
                        AddField("CARGO", sf.Cargo);
                        break;

                    case "towage":
                        var tow = cert as Certificate;
                        AddField("CERTIFICATE NO", tow.CertificateNumber);
                        AddField("JOB NO", tow.JobNumber);
                        AddField("PROJECT", tow.ProjectTitle);
                        AddField("CLIENT", tow.ClientName);
                        AddField("NAME OF TOW", tow.TowName);
                        AddField("FROM LOCATION", tow.ArrivalPort);
                        AddField("TO LOCATION", tow.DeparturePort);
                        AddField("TUG NAME", tow.TugName);
                        AddField("CARGO", tow.Cargo);
                        break;

                    case "loadout":
                        var load = cert as LoadoutCertificate;
                        AddField("CERTIFICATE NO", load.CertificateNumber);
                        AddField("JOB NO", load.JobNumber);
                        AddField("PROJECT", load.ProjectTitle);
                        AddField("CLIENT", load.Client);
                        AddField("CARGO", load.Cargo);
                        AddField("ONTO BARGE VESSEL", load.BargeVessel);
                        AddField("LOCATION", load.Location);
                        AddField("MODE OF OPERATION", load.OperationType);
                        break;
                }

                document.Add(new iText.Layout.Element.Paragraph("\n\nThis document is system-generated and valid without signature.")
                    .SetFont(normalFont)
                    .SetFontSize(9)
                    .SetTextAlignment(iText.Layout.Properties.TextAlignment.RIGHT)
                    .SetItalic());

                document.Close();
                return ms.ToArray();
            }
        }
    }
}
