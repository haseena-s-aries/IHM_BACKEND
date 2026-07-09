using iText.Kernel.Colors;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Survey_Certification_App.Data;
using Survey_Certification_App.Models.Survey;
using System.Security.Claims;
using Survey_Certification_App.Services;

namespace Survey_Certification_App.Controllers
{
    [Route("api/ex-survey")]
    [ApiController]
    public class ExSurveyApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly AccessScopeService _access;

        public ExSurveyApiController(ApplicationDbContext db, AccessScopeService access)
        {
            _db = db;
            _access = access;
        }

        [HttpGet("jobs/{jobId:int}/summary")]
        public async Task<IActionResult> GetSummary(int jobId)
        {
            var job = await _db.SurveyJobs.FirstOrDefaultAsync(x =>
                x.Id == jobId && x.ModuleType == SurveyModuleType.ExSurvey);

            if (job == null)
                return NotFound("Ex-Survey job not found.");

            var equipmentCount = await _db.ExEquipments.CountAsync(x =>
                x.SurveyJobId == jobId && x.IsActive);
            var areaCount = await _db.ExEquipments
                .Where(x => x.SurveyJobId == jobId && x.IsActive)
                .Select(x => x.Area ?? "Unassigned")
                .Distinct()
                .CountAsync();
            var answeredEquipmentCount = await _db.ExChecklistAnswers
                .Where(x => x.SurveyJobId == jobId)
                .Select(x => x.ExEquipmentId)
                .Distinct()
                .CountAsync();
            var answerCount = await _db.ExChecklistAnswers.CountAsync(x =>
                x.SurveyJobId == jobId);
            var failedAnswerCount = await _db.ExChecklistAnswers.CountAsync(x =>
                x.SurveyJobId == jobId && x.Result == ExChecklistResult.Fail);
            var openDefectCount = await _db.ExDefects.CountAsync(x =>
                x.SurveyJobId == jobId && x.Status == ExDefectStatus.Open);
            var photoCount = await _db.SurveyPhotos.CountAsync(x =>
                x.SurveyJobId == jobId && x.RelatedModule == "ExSurvey");

            return Ok(new ExSurveySummaryDto
            {
                SurveyJobId = jobId,
                Status = job.Status.ToString(),
                AreaCount = areaCount,
                EquipmentCount = equipmentCount,
                AnsweredEquipmentCount = answeredEquipmentCount,
                AnswerCount = answerCount,
                FailedAnswerCount = failedAnswerCount,
                OpenDefectCount = openDefectCount,
                PhotoCount = photoCount
            });
        }

        [HttpGet("jobs/{jobId:int}/equipment")]
        public async Task<IActionResult> GetEquipment(int jobId)
        {
            if (!await IsExSurveyJobAsync(jobId))
                return NotFound("Ex-Survey job not found.");

            var equipment = await _db.ExEquipments
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.Area)
                .ThenBy(x => x.EquipmentId)
                .Select(x => ToEquipmentDto(x))
                .ToListAsync();

            return Ok(equipment);
        }

        [HttpPost("jobs/{jobId:int}/equipment")]
        public async Task<IActionResult> CreateEquipment(
            int jobId,
            [FromBody] CreateExEquipmentRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.EquipmentId))
                return BadRequest("Equipment ID is required.");

            if (!await IsExSurveyJobAsync(jobId))
                return NotFound("Ex-Survey job not found.");

            var equipmentId = request.EquipmentId.Trim();
            var duplicate = await _db.ExEquipments.AnyAsync(x =>
                x.SurveyJobId == jobId && x.EquipmentId == equipmentId);

            if (duplicate)
                return BadRequest("Equipment ID already exists in this job.");

            var equipment = new ExEquipment
            {
                SurveyJobId = jobId,
                EquipmentId = equipmentId,
                RfidTag = Clean(request.RfidTag),
                Area = Clean(request.Area) ?? "Unassigned",
                Location = Clean(request.Location),
                EquipmentType = Clean(request.EquipmentType),
                Description = Clean(request.Description),
                Manufacturer = Clean(request.Manufacturer),
                Model = Clean(request.Model),
                SerialNumber = Clean(request.SerialNumber),
                CreatedAt = DateTime.UtcNow
            };

            _db.ExEquipments.Add(equipment);
            await _db.SaveChangesAsync();
            return Ok(ToEquipmentDto(equipment));
        }

        [HttpPut("equipment/{equipmentId:int}")]
        public async Task<IActionResult> UpdateEquipment(
            int equipmentId,
            [FromBody] UpdateExEquipmentRequest request)
        {
            var equipment = await _db.ExEquipments.FindAsync(equipmentId);
            if (equipment == null)
                return NotFound("Equipment not found.");

            equipment.RfidTag = Clean(request.RfidTag);
            equipment.Area = Clean(request.Area) ?? "Unassigned";
            equipment.Location = Clean(request.Location);
            equipment.EquipmentType = Clean(request.EquipmentType);
            equipment.Description = Clean(request.Description);
            equipment.Manufacturer = Clean(request.Manufacturer);
            equipment.Model = Clean(request.Model);
            equipment.SerialNumber = Clean(request.SerialNumber);
            equipment.IsActive = request.IsActive;

            await _db.SaveChangesAsync();
            return Ok(ToEquipmentDto(equipment));
        }

        [HttpGet("checklist/questions")]
        public async Task<IActionResult> GetChecklistQuestions(
            [FromQuery] string? inspectionType)
        {
            var normalizedType = NormalizeInspectionType(inspectionType);
            if (!string.IsNullOrWhiteSpace(inspectionType) && normalizedType == null)
                return BadRequest("Inspection type must be Visual, Close, or Detailed.");

            await EnsureDefaultChecklistQuestionsAsync();

            var query = _db.ExChecklistQuestions.Where(x => x.IsActive);
            if (normalizedType != null)
                query = query.Where(x => x.Section == normalizedType);

            var questions = await query
                .OrderBy(x => x.SortOrder)
                .Select(x => ToQuestionDto(x))
                .ToListAsync();

            return Ok(questions);
        }

        [HttpPost("checklist/questions")]
        public async Task<IActionResult> CreateChecklistQuestion(
            [FromBody] CreateExChecklistQuestionRequest request)
        {
            var inspectionType = NormalizeInspectionType(request.InspectionType);
            if (inspectionType == null)
                return BadRequest("Inspection type must be Visual, Close, or Detailed.");

            if (string.IsNullOrWhiteSpace(request.QuestionCode) ||
                string.IsNullOrWhiteSpace(request.QuestionText))
            {
                return BadRequest("Question code and question text are required.");
            }

            var questionCode = request.QuestionCode.Trim();
            var exists = await _db.ExChecklistQuestions.AnyAsync(x =>
                x.QuestionCode == questionCode);
            if (exists)
                return BadRequest("Checklist question code already exists.");

            var question = new ExChecklistQuestion
            {
                QuestionCode = questionCode,
                Section = inspectionType,
                QuestionText = request.QuestionText.Trim(),
                SortOrder = request.SortOrder,
                IsActive = true
            };

            _db.ExChecklistQuestions.Add(question);
            await _db.SaveChangesAsync();
            return Ok(ToQuestionDto(question));
        }

        [HttpGet("equipment/{equipmentId:int}/answers")]
        public async Task<IActionResult> GetAnswers(
            int equipmentId,
            [FromQuery] string? inspectionType)
        {
            var normalizedType = NormalizeInspectionType(inspectionType);
            if (!string.IsNullOrWhiteSpace(inspectionType) && normalizedType == null)
                return BadRequest("Inspection type must be Visual, Close, or Detailed.");

            var query = _db.ExChecklistAnswers
                .Include(x => x.ExChecklistQuestion)
                .Where(x => x.ExEquipmentId == equipmentId);

            if (normalizedType != null)
                query = query.Where(x => x.ExChecklistQuestion!.Section == normalizedType);

            var answers = await query
                .OrderBy(x => x.ExChecklistQuestion!.SortOrder)
                .Select(x => ToAnswerDto(x))
                .ToListAsync();

            return Ok(answers);
        }

        [HttpPost("checklist/answers")]
        public async Task<IActionResult> SaveAnswer([FromBody] SaveExChecklistAnswerRequest request)
        {
            if (request.Result == ExChecklistResult.Fail &&
                string.IsNullOrWhiteSpace(request.Comment))
            {
                return BadRequest("Comment is required when checklist result is Fail.");
            }

            var equipment = await _db.ExEquipments.FirstOrDefaultAsync(x =>
                x.Id == request.ExEquipmentId);
            if (equipment == null)
                return NotFound("Equipment not found.");

            var question = await _db.ExChecklistQuestions.FirstOrDefaultAsync(x =>
                x.Id == request.ExChecklistQuestionId && x.IsActive);
            if (question == null)
                return NotFound("Checklist question not found.");

            var answer = await _db.ExChecklistAnswers.FirstOrDefaultAsync(x =>
                x.ExEquipmentId == request.ExEquipmentId &&
                x.ExChecklistQuestionId == request.ExChecklistQuestionId);

            if (answer == null)
            {
                answer = new ExChecklistAnswer
                {
                    SurveyJobId = equipment.SurveyJobId,
                    ExEquipmentId = request.ExEquipmentId,
                    ExChecklistQuestionId = request.ExChecklistQuestionId,
                    AnsweredByUserId = request.AnsweredByUserId ?? GetCurrentUserId() ?? 0
                };
                _db.ExChecklistAnswers.Add(answer);
            }

            answer.Result = request.Result;
            answer.Comment = Clean(request.Comment);
            answer.AnsweredAt = DateTime.UtcNow;
            answer.ExChecklistQuestion = question;

            await _db.SaveChangesAsync();
            return Ok(ToAnswerDto(answer));
        }

        [HttpGet("jobs/{jobId:int}/defects")]
        public async Task<IActionResult> GetDefects(int jobId)
        {
            if (!await IsExSurveyJobAsync(jobId))
                return NotFound("Ex-Survey job not found.");

            var defects = await _db.ExDefects
                .Include(x => x.ExEquipment)
                .Where(x => x.SurveyJobId == jobId)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => ToDefectDto(x))
                .ToListAsync();

            return Ok(defects);
        }

        [HttpPost("defects")]
        public async Task<IActionResult> CreateDefect([FromBody] CreateExDefectRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Description))
                return BadRequest("Defect description is required.");

            var equipment = await _db.ExEquipments.FirstOrDefaultAsync(x =>
                x.Id == request.ExEquipmentId);
            if (equipment == null)
                return NotFound("Equipment not found.");

            var defect = new ExDefect
            {
                SurveyJobId = equipment.SurveyJobId,
                ExEquipmentId = request.ExEquipmentId,
                DefectNumber = Clean(request.DefectNumber),
                Description = request.Description.Trim(),
                Severity = Clean(request.Severity),
                Recommendation = Clean(request.Recommendation),
                Status = ExDefectStatus.Open,
                CreatedByUserId = request.CreatedByUserId ?? GetCurrentUserId() ?? 0,
                CreatedAt = DateTime.UtcNow
            };

            _db.ExDefects.Add(defect);
            await _db.SaveChangesAsync();
            defect.ExEquipment = equipment;
            return Ok(ToDefectDto(defect));
        }

        [HttpPost("defects/{defectId:int}/close")]
        public async Task<IActionResult> CloseDefect(int defectId)
        {
            return await SetDefectStatus(defectId, ExDefectStatus.Closed);
        }

        [HttpPost("defects/{defectId:int}/status")]
        public async Task<IActionResult> UpdateDefectStatus(
            int defectId,
            [FromBody] UpdateExDefectStatusRequest request)
        {
            if (!Enum.TryParse<ExDefectStatus>(request.Status, true, out var status))
                return BadRequest("Defect status must be Open, Closed, or Accepted.");

            return await SetDefectStatus(defectId, status);
        }

        [HttpGet("jobs/{jobId:int}/photos")]
        public async Task<IActionResult> GetPhotos(
            int jobId,
            [FromQuery] int? equipmentId)
        {
            if (!await IsExSurveyJobAsync(jobId))
                return NotFound("Ex-Survey job not found.");

            var query = _db.SurveyPhotos.Where(x =>
                x.SurveyJobId == jobId && x.RelatedModule == "ExSurvey");
            if (equipmentId.HasValue)
                query = query.Where(x => x.RelatedRecordId == equipmentId.Value);

            var photos = await query
                .OrderByDescending(x => x.UploadedAt)
                .Select(x => new ExSurveyPhotoDto
                {
                    Id = x.Id,
                    SurveyJobId = x.SurveyJobId,
                    EquipmentId = x.RelatedRecordId,
                    OriginalFileName = x.OriginalFileName,
                    FilePath = x.FilePath,
                    ContentType = x.ContentType,
                    UploadedByUserId = x.UploadedByUserId,
                    UploadedAt = x.UploadedAt
                })
                .ToListAsync();

            return Ok(photos);
        }

        [HttpGet("jobs/{jobId:int}/documents")]
        public async Task<IActionResult> GetDocuments(int jobId)
        {
            if (!await IsExSurveyJobAsync(jobId))
                return NotFound("Ex-Survey job not found.");

            var documents = await _db.SurveyUploadedFiles
                .Where(x => x.SurveyJobId == jobId &&
                            x.FileType != null &&
                            x.FileType.StartsWith("ExSurvey:"))
                .OrderByDescending(x => x.UploadedAt)
                .Select(x => new ExSurveyDocumentDto
                {
                    Id = x.Id,
                    SurveyJobId = x.SurveyJobId,
                    DocumentType = x.FileType!.Substring("ExSurvey:".Length),
                    OriginalFileName = x.OriginalFileName,
                    FilePath = x.FilePath,
                    ContentType = x.ContentType,
                    SizeBytes = x.SizeBytes,
                    UploadedByUserId = x.UploadedByUserId,
                    UploadedAt = x.UploadedAt
                })
                .ToListAsync();

            return Ok(documents);
        }

        [HttpPost("jobs/{jobId:int}/documents")]
        public async Task<IActionResult> UploadDocument(
            int jobId,
            IFormFile file,
            [FromForm] string documentType,
            [FromForm] int? uploadedByUserId)
        {
            if (!await IsExSurveyJobAsync(jobId))
                return NotFound("Ex-Survey job not found.");

            if (file == null || file.Length == 0)
                return BadRequest("Document file is required.");

            var normalizedType = NormalizeDocumentType(documentType);
            if (normalizedType == null)
            {
                return BadRequest(
                    "Document type must be Drawing, EquipmentRegister, or ChecklistTemplate.");
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExtensions = new[]
            {
                ".pdf", ".jpg", ".jpeg", ".png", ".xlsx", ".xls", ".csv"
            };
            if (!allowedExtensions.Contains(extension))
                return BadRequest("Unsupported document type.");

            var uploadFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "uploads",
                "ex-survey",
                jobId.ToString(),
                "documents");
            Directory.CreateDirectory(uploadFolder);

            var storedFileName = $"{Guid.NewGuid():N}{extension}";
            var absolutePath = Path.Combine(uploadFolder, storedFileName);
            await using (var stream = new FileStream(absolutePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var document = new SurveyUploadedFile
            {
                SurveyJobId = jobId,
                OriginalFileName = file.FileName,
                StoredFileName = storedFileName,
                FilePath = $"/uploads/ex-survey/{jobId}/documents/{storedFileName}",
                FileType = $"ExSurvey:{normalizedType}",
                ContentType = file.ContentType,
                SizeBytes = file.Length,
                UploadedByUserId = uploadedByUserId ?? GetCurrentUserId() ?? 0,
                UploadedAt = DateTime.UtcNow
            };

            _db.SurveyUploadedFiles.Add(document);
            await _db.SaveChangesAsync();

            return Ok(new ExSurveyDocumentDto
            {
                Id = document.Id,
                SurveyJobId = document.SurveyJobId,
                DocumentType = normalizedType,
                OriginalFileName = document.OriginalFileName,
                FilePath = document.FilePath,
                ContentType = document.ContentType,
                SizeBytes = document.SizeBytes,
                UploadedByUserId = document.UploadedByUserId,
                UploadedAt = document.UploadedAt
            });
        }

        [HttpPost("jobs/{jobId:int}/equipment/{equipmentId:int}/photos")]
        public async Task<IActionResult> UploadEquipmentPhoto(
            int jobId,
            int equipmentId,
            IFormFile file,
            [FromForm] int? uploadedByUserId)
        {
            var equipment = await _db.ExEquipments.FirstOrDefaultAsync(x =>
                x.Id == equipmentId && x.SurveyJobId == jobId);
            if (equipment == null)
                return NotFound("Equipment not found in this Ex-Survey job.");

            if (file == null || file.Length == 0)
                return BadRequest("Photo file is required.");

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            if (!allowedExtensions.Contains(extension))
                return BadRequest("Only JPG, JPEG, PNG, and WEBP images are allowed.");

            var uploadFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "uploads",
                "ex-survey",
                jobId.ToString(),
                equipmentId.ToString());
            Directory.CreateDirectory(uploadFolder);

            var storedFileName = $"{Guid.NewGuid():N}{extension}";
            var absolutePath = Path.Combine(uploadFolder, storedFileName);
            await using (var stream = new FileStream(absolutePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var photo = new SurveyPhoto
            {
                SurveyJobId = jobId,
                RelatedModule = "ExSurvey",
                RelatedRecordId = equipmentId,
                OriginalFileName = file.FileName,
                StoredFileName = storedFileName,
                FilePath = $"/uploads/ex-survey/{jobId}/{equipmentId}/{storedFileName}",
                ContentType = file.ContentType,
                UploadedByUserId = uploadedByUserId ?? GetCurrentUserId() ?? 0,
                UploadedAt = DateTime.UtcNow
            };

            _db.SurveyPhotos.Add(photo);
            await _db.SaveChangesAsync();
            return Ok(ToPhotoDto(photo));
        }

        [HttpPost("jobs/{jobId:int}/submit")]
        public async Task<IActionResult> SubmitForQc(int jobId)
        {
            var job = await _db.SurveyJobs.FirstOrDefaultAsync(x =>
                x.Id == jobId && x.ModuleType == SurveyModuleType.ExSurvey);
            if (job == null)
                return NotFound("Ex-Survey job not found.");

            var equipmentIds = await _db.ExEquipments
                .Where(x => x.SurveyJobId == jobId && x.IsActive)
                .Select(x => x.Id)
                .ToListAsync();
            if (equipmentIds.Count == 0)
                return BadRequest("Add at least one equipment item before submitting.");

            var answeredEquipmentIds = await _db.ExChecklistAnswers
                .Where(x => x.SurveyJobId == jobId)
                .Select(x => x.ExEquipmentId)
                .Distinct()
                .ToListAsync();
            var incompleteCount = equipmentIds.Except(answeredEquipmentIds).Count();
            if (incompleteCount > 0)
                return BadRequest($"{incompleteCount} equipment item(s) do not have checklist answers.");

            job.Status = SurveyJobStatus.Submitted;
            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { job.Id, Status = job.Status.ToString() });
        }

        [HttpPost("jobs/{jobId:int}/review")]
        public async Task<IActionResult> ReviewJob(
            int jobId,
            [FromBody] ReviewExSurveyRequest request)
        {
            var job = await _db.SurveyJobs.FirstOrDefaultAsync(x =>
                x.Id == jobId && x.ModuleType == SurveyModuleType.ExSurvey);
            if (job == null)
                return NotFound("Ex-Survey job not found.");

            if (!Enum.TryParse<SurveyJobStatus>(request.Status, true, out var status) ||
                (status != SurveyJobStatus.Approved &&
                 status != SurveyJobStatus.QcReturned))
            {
                return BadRequest("QC status must be Approved or QcReturned.");
            }

            job.Status = status;
            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { job.Id, Status = job.Status.ToString() });
        }

        [HttpGet("jobs/{jobId:int}/report")]
        public async Task<IActionResult> GenerateReport(int jobId)
        {
            var job = await _db.SurveyJobs.FirstOrDefaultAsync(x =>
                x.Id == jobId && x.ModuleType == SurveyModuleType.ExSurvey);
            if (job == null)
                return NotFound("Ex-Survey job not found.");

            var equipment = await _db.ExEquipments
                .Where(x => x.SurveyJobId == jobId && x.IsActive)
                .OrderBy(x => x.Area)
                .ThenBy(x => x.EquipmentId)
                .ToListAsync();
            var answers = await _db.ExChecklistAnswers
                .Include(x => x.ExChecklistQuestion)
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.ExChecklistQuestion!.SortOrder)
                .ToListAsync();
            var defects = await _db.ExDefects
                .Include(x => x.ExEquipment)
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.DefectNumber)
                .ToListAsync();

            using var stream = new MemoryStream();
            using (var writer = new PdfWriter(stream))
            using (var pdf = new PdfDocument(writer))
            using (var document = new Document(pdf))
            {
                document.SetMargins(36, 36, 36, 36);
                document.Add(new Paragraph("EX-SURVEY REPORT")
                    .SetFontSize(20)
                    .SetBold()
                    .SetFontColor(new DeviceRgb(11, 29, 51))
                    .SetTextAlignment(TextAlignment.CENTER));
                document.Add(new Paragraph(
                        $"{job.JobNumber} | {job.ProjectName ?? job.VesselName ?? job.SiteName ?? "Project"}")
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetFontColor(ColorConstants.DARK_GRAY));

                AddKeyValueTable(document, new[]
                {
                    ("Client", job.ClientName),
                    ("Vessel / Site", job.VesselName ?? job.SiteName),
                    ("Inspection dates", FormatDateRange(job.StartDate, job.EndDate)),
                    ("Status", job.Status.ToString())
                });

                document.Add(SectionHeading("Equipment Register"));
                var equipmentTable = new Table(UnitValue.CreatePercentArray(
                        new float[] { 1.2f, 1.5f, 1.8f, 1.5f, 2f }))
                    .UseAllAvailableWidth();
                AddHeaderCells(
                    equipmentTable,
                    "Equipment ID",
                    "Area",
                    "Location",
                    "Type",
                    "Description");
                foreach (var item in equipment)
                {
                    AddBodyCells(
                        equipmentTable,
                        item.EquipmentId,
                        item.Area,
                        item.Location,
                        item.EquipmentType,
                        item.Description);
                }
                document.Add(equipmentTable);

                document.Add(SectionHeading("Area-wise Checklists"));
                foreach (var areaGroup in equipment.GroupBy(x => x.Area ?? "Unassigned"))
                {
                    document.Add(new Paragraph(areaGroup.Key).SetBold().SetFontSize(12));
                    foreach (var item in areaGroup)
                    {
                        document.Add(new Paragraph(
                                $"{item.EquipmentId} - {item.Description ?? item.EquipmentType ?? "Equipment"}")
                            .SetBold()
                            .SetFontSize(10));
                        var answerTable = new Table(UnitValue.CreatePercentArray(
                                new float[] { 1f, 3.8f, 1f, 2.2f }))
                            .UseAllAvailableWidth();
                        AddHeaderCells(answerTable, "Type", "Checklist point", "Result", "Comment");
                        foreach (var answer in answers.Where(x => x.ExEquipmentId == item.Id))
                        {
                            AddBodyCells(
                                answerTable,
                                answer.ExChecklistQuestion?.Section,
                                answer.ExChecklistQuestion?.QuestionText,
                                answer.Result.ToString(),
                                answer.Comment);
                        }
                        document.Add(answerTable);
                    }
                }

                document.Add(SectionHeading("Defect Register"));
                var defectTable = new Table(UnitValue.CreatePercentArray(
                        new float[] { 1f, 1.2f, 3f, 1.2f, 2.2f }))
                    .UseAllAvailableWidth();
                AddHeaderCells(
                    defectTable,
                    "Defect No.",
                    "Equipment",
                    "Description",
                    "Severity",
                    "Recommendation");
                foreach (var defect in defects)
                {
                    AddBodyCells(
                        defectTable,
                        defect.DefectNumber,
                        defect.ExEquipment?.EquipmentId,
                        defect.Description,
                        defect.Severity,
                        defect.Recommendation);
                }
                document.Add(defectTable);

                document.Add(new Paragraph($"Generated UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm}")
                    .SetFontSize(8)
                    .SetFontColor(ColorConstants.GRAY)
                    .SetTextAlignment(TextAlignment.RIGHT));
            }

            job.Status = SurveyJobStatus.ReportGenerated;
            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return File(
                stream.ToArray(),
                "application/pdf",
                $"Ex-Survey-{SanitizeFileName(job.JobNumber)}.pdf");
        }

        private async Task<IActionResult> SetDefectStatus(
            int defectId,
            ExDefectStatus status)
        {
            var defect = await _db.ExDefects
                .Include(x => x.ExEquipment)
                .FirstOrDefaultAsync(x => x.Id == defectId);
            if (defect == null)
                return NotFound("Defect not found.");

            defect.Status = status;
            defect.ClosedAt = status == ExDefectStatus.Open ? null : DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(ToDefectDto(defect));
        }

        private int? GetCurrentUserId()
        {
            var claimValue =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub") ??
                User.FindFirstValue("userId");

            return int.TryParse(claimValue, out var userId) ? userId : null;
        }

        private Task<bool> IsExSurveyJobAsync(int jobId)
{
            return _access
                .FilterJobs(_db.SurveyJobs.AsQueryable())
                .AnyAsync(x => x.Id == jobId && x.ModuleType == SurveyModuleType.ExSurvey);
        }

        private async Task EnsureDefaultChecklistQuestionsAsync()
        {
            var existingCodes = await _db.ExChecklistQuestions
                .Select(x => x.QuestionCode)
                .ToListAsync();
            var defaults = new[]
            {
                ("VIS-01", "Visual", "Equipment identification and tag details are legible.", 10),
                ("VIS-02", "Visual", "Equipment is free from visible damage, corrosion, or unauthorized modification.", 20),
                ("VIS-03", "Visual", "Cable entries, glands, blanking elements, and fasteners appear secure.", 30),
                ("VIS-04", "Visual", "Area classification and equipment Ex marking are suitable for the location.", 40),
                ("CLO-01", "Close", "Enclosure, covers, bolts, and threaded joints are complete and correctly secured.", 110),
                ("CLO-02", "Close", "Cable glands and stopping plugs are the correct type and show no signs of loosening.", 120),
                ("CLO-03", "Close", "Earthing and bonding connections are present and in sound condition.", 130),
                ("CLO-04", "Close", "No excessive dust, contamination, moisture ingress, or deterioration is evident.", 140),
                ("DET-01", "Detailed", "Internal terminals, conductors, insulation, and clearances are in satisfactory condition.", 210),
                ("DET-02", "Detailed", "Flamepaths, gaskets, seals, and mating surfaces are clean and undamaged.", 220),
                ("DET-03", "Detailed", "Protection settings, certificates, and equipment documentation match the installation.", 230),
                ("DET-04", "Detailed", "Tests and measurements required by the inspection scope are satisfactory.", 240)
            };

            var additions = defaults
                .Where(x => !existingCodes.Contains(x.Item1))
                .Select(x => new ExChecklistQuestion
                {
                    QuestionCode = x.Item1,
                    Section = x.Item2,
                    QuestionText = x.Item3,
                    SortOrder = x.Item4,
                    IsActive = true
                })
                .ToList();

            if (additions.Count == 0)
                return;

            _db.ExChecklistQuestions.AddRange(additions);
            await _db.SaveChangesAsync();
        }

        private static string? NormalizeInspectionType(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim().ToLowerInvariant() switch
            {
                "visual" => "Visual",
                "close" => "Close",
                "detailed" => "Detailed",
                _ => null
            };
        }

        private static string? NormalizeDocumentType(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim().ToLowerInvariant() switch
            {
                "drawing" => "Drawing",
                "equipmentregister" => "EquipmentRegister",
                "equipment register" => "EquipmentRegister",
                "checklisttemplate" => "ChecklistTemplate",
                "checklist template" => "ChecklistTemplate",
                _ => null
            };
        }

        private static string? Clean(string? value)
        {
            var cleaned = value?.Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
        }

        private static Paragraph SectionHeading(string text)
        {
            return new Paragraph(text)
                .SetFontSize(14)
                .SetBold()
                .SetFontColor(new DeviceRgb(25, 118, 210))
                .SetMarginTop(16)
                .SetMarginBottom(8);
        }

        private static void AddKeyValueTable(
            Document document,
            IEnumerable<(string Label, string? Value)> rows)
        {
            var table = new Table(UnitValue.CreatePercentArray(new float[] { 1.4f, 3.6f }))
                .UseAllAvailableWidth()
                .SetMarginTop(16);
            foreach (var row in rows)
            {
                table.AddCell(new Cell()
                    .Add(new Paragraph(row.Label).SetBold())
                    .SetBackgroundColor(new DeviceRgb(235, 241, 248)));
                table.AddCell(new Cell().Add(new Paragraph(row.Value ?? "-")));
            }
            document.Add(table);
        }

        private static void AddHeaderCells(Table table, params string[] values)
        {
            foreach (var value in values)
            {
                table.AddHeaderCell(new Cell()
                    .Add(new Paragraph(value).SetBold().SetFontSize(8))
                    .SetBackgroundColor(new DeviceRgb(11, 29, 51))
                    .SetFontColor(ColorConstants.WHITE));
            }
        }

        private static void AddBodyCells(Table table, params string?[] values)
        {
            foreach (var value in values)
                table.AddCell(new Cell().Add(new Paragraph(value ?? "-").SetFontSize(8)));
        }

        private static string FormatDateRange(DateTime? start, DateTime? end)
        {
            if (start == null && end == null)
                return "-";
            if (start == null)
                return end!.Value.ToString("yyyy-MM-dd");
            if (end == null)
                return start.Value.ToString("yyyy-MM-dd");
            return $"{start.Value:yyyy-MM-dd} to {end.Value:yyyy-MM-dd}";
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(x => invalid.Contains(x) ? '-' : x).ToArray());
        }

        private static ExEquipmentDto ToEquipmentDto(ExEquipment item)
        {
            return new ExEquipmentDto
            {
                Id = item.Id,
                SurveyJobId = item.SurveyJobId,
                EquipmentId = item.EquipmentId,
                RfidTag = item.RfidTag,
                Area = item.Area,
                Location = item.Location,
                EquipmentType = item.EquipmentType,
                Description = item.Description,
                Manufacturer = item.Manufacturer,
                Model = item.Model,
                SerialNumber = item.SerialNumber,
                IsActive = item.IsActive
            };
        }

        private static ExChecklistQuestionDto ToQuestionDto(ExChecklistQuestion item)
        {
            return new ExChecklistQuestionDto
            {
                Id = item.Id,
                QuestionCode = item.QuestionCode,
                InspectionType = item.Section,
                QuestionText = item.QuestionText,
                SortOrder = item.SortOrder
            };
        }

        private static ExChecklistAnswerDto ToAnswerDto(ExChecklistAnswer item)
        {
            return new ExChecklistAnswerDto
            {
                Id = item.Id,
                SurveyJobId = item.SurveyJobId,
                ExEquipmentId = item.ExEquipmentId,
                ExChecklistQuestionId = item.ExChecklistQuestionId,
                QuestionCode = item.ExChecklistQuestion?.QuestionCode,
                InspectionType = item.ExChecklistQuestion?.Section,
                QuestionText = item.ExChecklistQuestion?.QuestionText,
                Result = item.Result.ToString(),
                Comment = item.Comment,
                AnsweredByUserId = item.AnsweredByUserId,
                AnsweredAt = item.AnsweredAt
            };
        }

        private static ExDefectDto ToDefectDto(ExDefect item)
        {
            return new ExDefectDto
            {
                Id = item.Id,
                SurveyJobId = item.SurveyJobId,
                ExEquipmentId = item.ExEquipmentId,
                EquipmentId = item.ExEquipment?.EquipmentId,
                DefectNumber = item.DefectNumber,
                Description = item.Description,
                Severity = item.Severity,
                Recommendation = item.Recommendation,
                Status = item.Status.ToString(),
                CreatedByUserId = item.CreatedByUserId,
                CreatedAt = item.CreatedAt
            };
        }

        private static ExSurveyPhotoDto ToPhotoDto(SurveyPhoto photo)
        {
            return new ExSurveyPhotoDto
            {
                Id = photo.Id,
                SurveyJobId = photo.SurveyJobId,
                EquipmentId = photo.RelatedRecordId,
                OriginalFileName = photo.OriginalFileName,
                FilePath = photo.FilePath,
                ContentType = photo.ContentType,
                UploadedByUserId = photo.UploadedByUserId,
                UploadedAt = photo.UploadedAt
            };
        }
    }

    public class CreateExEquipmentRequest
    {
        public string EquipmentId { get; set; } = string.Empty;
        public string? RfidTag { get; set; }
        public string? Area { get; set; }
        public string? Location { get; set; }
        public string? EquipmentType { get; set; }
        public string? Description { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
    }

    public class UpdateExEquipmentRequest : CreateExEquipmentRequest
    {
        public bool IsActive { get; set; } = true;
    }

    public class CreateExChecklistQuestionRequest
    {
        public string QuestionCode { get; set; } = string.Empty;
        public string InspectionType { get; set; } = string.Empty;
        public string QuestionText { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }

    public class SaveExChecklistAnswerRequest
    {
        public int ExEquipmentId { get; set; }
        public int ExChecklistQuestionId { get; set; }
        public ExChecklistResult Result { get; set; }
        public string? Comment { get; set; }
        public int? AnsweredByUserId { get; set; }
    }

    public class CreateExDefectRequest
    {
        public int ExEquipmentId { get; set; }
        public string? DefectNumber { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? Severity { get; set; }
        public string? Recommendation { get; set; }
        public int? CreatedByUserId { get; set; }
    }

    public class UpdateExDefectStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }

    public class ReviewExSurveyRequest
    {
        public string Status { get; set; } = string.Empty;
        public string? Comment { get; set; }
    }

    public class ExEquipmentDto
    {
        public int Id { get; set; }
        public int SurveyJobId { get; set; }
        public string EquipmentId { get; set; } = string.Empty;
        public string? RfidTag { get; set; }
        public string? Area { get; set; }
        public string? Location { get; set; }
        public string? EquipmentType { get; set; }
        public string? Description { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public bool IsActive { get; set; }
    }

    public class ExChecklistQuestionDto
    {
        public int Id { get; set; }
        public string QuestionCode { get; set; } = string.Empty;
        public string? InspectionType { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }

    public class ExChecklistAnswerDto
    {
        public int Id { get; set; }
        public int SurveyJobId { get; set; }
        public int ExEquipmentId { get; set; }
        public int ExChecklistQuestionId { get; set; }
        public string? QuestionCode { get; set; }
        public string? InspectionType { get; set; }
        public string? QuestionText { get; set; }
        public string Result { get; set; } = string.Empty;
        public string? Comment { get; set; }
        public int AnsweredByUserId { get; set; }
        public DateTime AnsweredAt { get; set; }
    }

    public class ExDefectDto
    {
        public int Id { get; set; }
        public int SurveyJobId { get; set; }
        public int ExEquipmentId { get; set; }
        public string? EquipmentId { get; set; }
        public string? DefectNumber { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? Severity { get; set; }
        public string? Recommendation { get; set; }
        public string Status { get; set; } = string.Empty;
        public int CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ExSurveySummaryDto
    {
        public int SurveyJobId { get; set; }
        public string Status { get; set; } = string.Empty;
        public int AreaCount { get; set; }
        public int EquipmentCount { get; set; }
        public int AnsweredEquipmentCount { get; set; }
        public int AnswerCount { get; set; }
        public int FailedAnswerCount { get; set; }
        public int OpenDefectCount { get; set; }
        public int PhotoCount { get; set; }
    }

    public class ExSurveyPhotoDto
    {
        public int Id { get; set; }
        public int SurveyJobId { get; set; }
        public int? EquipmentId { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public int UploadedByUserId { get; set; }
        public DateTime UploadedAt { get; set; }
    }

    public class ExSurveyDocumentDto
    {
        public int Id { get; set; }
        public int SurveyJobId { get; set; }
        public string DocumentType { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public long SizeBytes { get; set; }
        public int UploadedByUserId { get; set; }
        public DateTime UploadedAt { get; set; }
    }
}
