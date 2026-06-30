using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Survey_Certification_App.Data;
using Survey_Certification_App.IemailserviceInterface;
using Survey_Certification_App.Models;
using Survey_Certification_App.Models.Survey;
using System.Net;
using System.Security.Claims;

namespace Survey_Certification_App.Controllers
{
    [Route("api/survey")]
    [ApiController]
    public class SurveyController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IEmailService _emailService;
        private readonly ILogger<SurveyController> _logger;

        public SurveyController(
            ApplicationDbContext db,
            IEmailService emailService,
            ILogger<SurveyController> logger)
        {
            _db = db;
            _emailService = emailService;
            _logger = logger;
        }

        //   [HttpGet("jobs")]
        //   public async Task<IActionResult> GetJobs(
        //       [FromQuery] SurveyModuleType? moduleType,
        //       [FromQuery] SurveyJobStatus? status)
        //   {
        //       var query = _db.SurveyJobs
        //           .Include(x => x.Assignments)
        //           .AsQueryable();

        //       if (moduleType.HasValue)
        //           query = query.Where(x => x.ModuleType == moduleType.Value);

        //       if (status.HasValue)
        //           query = query.Where(x => x.Status == status.Value);

        //       var jobs = await query
        //.OrderByDescending(x => x.CreatedAt)
        //.ToListAsync();

        //       return Ok(await ToDtosAsync(jobs));


        //   }



        [HttpGet("jobs/my")]
        public async Task<IActionResult> GetMyJobs([FromQuery] int? surveyorUserId)
        {
            var currentUserId = surveyorUserId ?? GetCurrentUserId();

            if (currentUserId == null || currentUserId <= 0)
                return BadRequest("Surveyor user id is required.");

            var jobs = await _db.SurveyJobs
                .Include(x => x.Assignments)
                .Where(x => x.Assignments.Any(a => a.SurveyorUserId == currentUserId))
                .OrderByDescending(x => x.CreatedAt)

                .ToListAsync();

            return Ok(await ToDtosAsync(jobs));
        }

        [HttpGet("jobs/{id:int}")]
        public async Task<IActionResult> GetJobById(int id)
        {
            var job = await _db.SurveyJobs
                .Include(x => x.Assignments)
                .Include(x => x.UploadedFiles)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (job == null)
                return NotFound("Survey job not found.");

            var userIds = job.Assignments
            .Select(a => a.SurveyorUserId)
            .Concat(job.Assignments.Select(a => a.AssignedByUserId))
            .Distinct()
            .ToList();

            var userNames = await _db.users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(
                    u => u.Id,
                    u => !string.IsNullOrWhiteSpace(u.FullName)
                        ? u.FullName
                        : (!string.IsNullOrWhiteSpace(u.Email)
                            ? u.Email
                            : $"User {u.Id}")
                );

            return Ok(ToDto(job, userNames));
        }

        [HttpPost("jobs")]
        public async Task<IActionResult> CreateJob([FromBody] CreateSurveyJobRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var exists = await _db.SurveyJobs.AnyAsync(x =>
                x.ModuleType == request.ModuleType &&
                x.JobNumber == request.JobNumber);

            if (exists)
                return BadRequest("A survey job with this job number already exists.");

            var createdBy = request.CreatedByUserId ?? GetCurrentUserId() ?? 0;


            Vessel? vessel = null;
            if (request.VesselId.HasValue && request.VesselId.Value > 0)
            {
                vessel = await _db.Vessels.FirstOrDefaultAsync(x => x.Id == request.VesselId.Value);

                if (vessel == null)
                    return BadRequest("Selected vessel not found.");
            }

            var job = new SurveyJob
            {
                ModuleType = request.ModuleType,
                JobNumber = request.JobNumber.Trim(),
                ClientName = vessel?.ClientName ?? request.ClientName,
                ProjectName = request.ProjectName,
                AuditPort = request.AuditPort,
                AuditAddress = request.AuditAddress,


                VesselId = vessel?.Id,
                VesselName = vessel?.VesselName ?? request.VesselName,
                ImoNumber = vessel?.ImoNumber ?? request.ImoNumber,
                VesselSnapshotJson = vessel == null ? null : System.Text.Json.JsonSerializer.Serialize(new
                {
                    vessel.Id,
                    vessel.VesselName,
                    vessel.ImoNumber,
                    vessel.OfficialNumber,
                    vessel.VesselType,
                    vessel.Flag,
                    vessel.PortOfRegistry,
                    vessel.ClassSociety,
                    vessel.GrossTonnage,
                    vessel.NetTonnage,
                    vessel.Deadweight,
                    vessel.LengthOverall,
                    vessel.Breadth,
                    vessel.Depth,
                    vessel.YearBuilt,
                    vessel.OwnerName,
                    vessel.ManagerName,
                    vessel.ClientName
                }),

                SiteName = request.SiteName,
                StartDate = request.StartDate,
        
                EndDate = request.EndDate,
                Status = SurveyJobStatus.Draft,
                CreatedByUserId = createdBy,
                CreatedAt = DateTime.UtcNow
            };

            _db.SurveyJobs.Add(job);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetJobById), new { id = job.Id }, ToDto(job));
        }


        [HttpPut("jobs/{id:int}")]
        public async Task<IActionResult> UpdateJob(int id, [FromBody] UpdateSurveyJobRequest request)
        {
            var job = await _db.SurveyJobs.FindAsync(id);

            if (job == null)
                return NotFound("Survey job not found.");

            job.ClientName = request.ClientName;
            job.ProjectName = request.ProjectName;
            job.VesselName = request.VesselName;
            job.ImoNumber = request.ImoNumber;
            job.SiteName = request.SiteName;
            job.StartDate = request.StartDate;
          
            job.EndDate = request.EndDate;
            job.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(ToDto(job));
        }

        [HttpPost("jobs/{id:int}/assign")]
        public async Task<IActionResult> AssignJob(int id, [FromBody] AssignSurveyJobRequest request)
        {
            var job = await _db.SurveyJobs
                .Include(x => x.Assignments)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (job == null)
                return NotFound("Survey job not found.");

            var alreadyAssigned = job.Assignments.Any(x =>
  x.SurveyorUserId == request.SurveyorUserId &&
  x.Status != SurveyAssignmentStatus.Returned);

            if (alreadyAssigned)
                return BadRequest("This surveyor is already assigned to this job.");

            var surveyor = await _db.users.FindAsync(request.SurveyorUserId);
            if (surveyor == null)
                return BadRequest("Selected surveyor not found.");

            var assignedBy = request.AssignedByUserId ?? GetCurrentUserId() ?? 0;
            var assignedByName = await GetUserDisplayNameAsync(assignedBy);

            var assignment = new SurveyAssignment
            {
                SurveyJobId = job.Id,
                SurveyorUserId = request.SurveyorUserId,
                AssignedByUserId = assignedBy,
                AssignedAt = DateTime.UtcNow,
                Status = SurveyAssignmentStatus.Assigned
            };

            job.Assignments.Add(assignment);
            job.Status = SurveyJobStatus.Assigned;
            job.UpdatedAt = DateTime.UtcNow;
            _db.UserNotifications.Add(CreateAssignmentNotification(
                surveyor.Id,
                job,
                "Surveyor",
                assignedByName,
                isReassignment: false));

            await _db.SaveChangesAsync();
            await TrySendAssignmentEmailAsync(
                surveyor,
                job,
                "Surveyor",
                assignedByName,
                isReassignment: false);

            return Ok(ToDto(job));
        }
        [HttpPost("jobs/{id:int}/survey-date")]
        public async Task<IActionResult> UpdateSurveyDate(int id, [FromBody] UpdateSurveyDateRequest request)
        {
            var job = await _db.SurveyJobs.FindAsync(id);

            if (job == null)
                return NotFound("Survey job not found.");

            if (request.SurveyDate == null)
                return BadRequest("Survey date is required.");

            job.SurveyDate = request.SurveyDate;
            job.Status = SurveyJobStatus.InProgress;
            job.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(ToDto(job));
        }
        [HttpPost("jobs/{id:int}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateSurveyJobStatusRequest request)
        {
            var job = await _db.SurveyJobs.FindAsync(id);

            if (job == null)
                return NotFound("Survey job not found.");

            job.Status = request.Status;

            if (request.Status == SurveyJobStatus.InProgress && job.SurveyDate == null)
            {
                job.SurveyDate = request.SurveyDate ?? DateTime.Today;
            }

            job.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(ToDto(job));
        }

        [HttpPost("jobs/{id:int}/photos")]
        public async Task<IActionResult> UploadPhoto(
                int id,
                IFormFile file,
                [FromForm] int? relatedRecordId,
                [FromForm] int? uploadedByUserId,
                [FromForm] decimal? latitude,
                [FromForm] decimal? longitude)
        {
            var job = await _db.SurveyJobs.FirstOrDefaultAsync(x => x.Id == id);

            if (job == null)
                return NotFound("Survey job not found.");

            if (file == null || file.Length == 0)
                return BadRequest("Photo file is required.");

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var extension = Path.GetExtension(file.FileName).ToLower();

            if (!allowedExtensions.Contains(extension))
                return BadRequest("Only image files are allowed.");

            var uploadFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "uploads",
                "survey-photos",
                id.ToString()
            );

            Directory.CreateDirectory(uploadFolder);

            var storedFileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadFolder, storedFileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var relativePath = $"/uploads/survey-photos/{id}/{storedFileName}";

            var photo = new SurveyPhoto
            {
                SurveyJobId = id,
                RelatedModule = job.ModuleType.ToString(),
                RelatedRecordId = relatedRecordId,
                OriginalFileName = file.FileName,
                StoredFileName = storedFileName,
                FilePath = relativePath,
                ContentType = file.ContentType,
                Latitude = latitude,
                Longitude = longitude,
                UploadedByUserId = uploadedByUserId ?? GetCurrentUserId() ?? 0,
                UploadedAt = DateTime.UtcNow
            };

            _db.SurveyPhotos.Add(photo);
            await _db.SaveChangesAsync();

            return Ok(new SurveyPhotoDto
            {
                Id = photo.Id,
                SurveyJobId = photo.SurveyJobId,
                RelatedModule = photo.RelatedModule,
                RelatedRecordId = photo.RelatedRecordId,
                OriginalFileName = photo.OriginalFileName,
                FilePath = photo.FilePath,
                ContentType = photo.ContentType,
                UploadedByUserId = photo.UploadedByUserId,
                UploadedAt = photo.UploadedAt
            });
        }
        [HttpGet("jobs/{id:int}/photos")]
        public async Task<IActionResult> GetPhotos(int id)
        {
            var photos = await _db.SurveyPhotos
                .Where(x => x.SurveyJobId == id)
                .OrderByDescending(x => x.UploadedAt)
                .Select(x => new SurveyPhotoDto
                {
                    Id = x.Id,
                    SurveyJobId = x.SurveyJobId,
                    RelatedModule = x.RelatedModule,
                    RelatedRecordId = x.RelatedRecordId,
                    OriginalFileName = x.OriginalFileName,
                    FilePath = x.FilePath,
                    ContentType = x.ContentType,
                    UploadedByUserId = x.UploadedByUserId,
                    UploadedAt = x.UploadedAt
                })
                .ToListAsync();

            return Ok(photos);
        }

        [HttpDelete("jobs/{id:int}")]
        public async Task<IActionResult> DeleteJob(int id)
        {
            var job = await _db.SurveyJobs.FirstOrDefaultAsync(x => x.Id == id);
            if (job == null)
                return NotFound("Survey job not found.");

            await using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                _db.IhmAppendixTwoAttachments.RemoveRange(
                    await _db.IhmAppendixTwoAttachments.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.IhmAppendixOneResultPoints.RemoveRange(
                    await _db.IhmAppendixOneResultPoints.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.IhmOhsRiskAssessmentRows.RemoveRange(
                    await _db.IhmOhsRiskAssessmentRows.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.IhmOhsPeople.RemoveRange(
                    await _db.IhmOhsPeople.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.IhmAppendixFiveInformationPoints.RemoveRange(
                    await _db.IhmAppendixFiveInformationPoints.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.IhmNecessaryInformationPoints.RemoveRange(
                    await _db.IhmNecessaryInformationPoints.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.IhmStructureHulls.RemoveRange(
                    await _db.IhmStructureHulls.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.IhmEquipmentMachineryReportRows.RemoveRange(
                    await _db.IhmEquipmentMachineryReportRows.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.IhmPaintCoatingSystems.RemoveRange(
                    await _db.IhmPaintCoatingSystems.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.IhmReportRevisions.RemoveRange(
                    await _db.IhmReportRevisions.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.IhmReportInfos.RemoveRange(
                    await _db.IhmReportInfos.Where(x => x.SurveyJobId == id).ToListAsync());

                _db.IhmAreaLayoutPlacements.RemoveRange(
                    await _db.IhmAreaLayoutPlacements.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.IhmSamplingPhotos.RemoveRange(
                    await _db.IhmSamplingPhotos.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.IhmImageMarkers.RemoveRange(
                    await _db.IhmImageMarkers.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.IhmCheckpointSubmissions.RemoveRange(
                    await _db.IhmCheckpointSubmissions.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.IhmCheckpoints.RemoveRange(
                    await _db.IhmCheckpoints.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.IhmAreas.RemoveRange(
                    await _db.IhmAreas.Where(x => x.SurveyJobId == id).ToListAsync());

                _db.ExChecklistAnswers.RemoveRange(
                    await _db.ExChecklistAnswers.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.ExDefects.RemoveRange(
                    await _db.ExDefects.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.ExEquipments.RemoveRange(
                    await _db.ExEquipments.Where(x => x.SurveyJobId == id).ToListAsync());

                _db.SurveyPhotos.RemoveRange(
                    await _db.SurveyPhotos.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.SurveyUploadedFiles.RemoveRange(
                    await _db.SurveyUploadedFiles.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.SurveyAssignments.RemoveRange(
                    await _db.SurveyAssignments.Where(x => x.SurveyJobId == id).ToListAsync());
                _db.UserNotifications.RemoveRange(
                    await _db.UserNotifications.Where(x => x.SurveyJobId == id).ToListAsync());

                _db.SurveyJobs.Remove(job);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { message = "Survey job deleted successfully." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to delete survey job {SurveyJobId}", id);
                return StatusCode(500, new
                {
                    message = "Failed to delete survey job.",
                    details = ex.Message
                });
            }
        }

        [HttpGet("jobs")]
        public async Task<IActionResult> GetJobs(
         [FromQuery] string? moduleType,
         [FromQuery] SurveyJobStatus? status,
         [FromQuery] int? assignedSurveyorUserId)
        {
            var query = _db.SurveyJobs
                .Include(x => x.Assignments)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(moduleType))
            {
                SurveyModuleType parsedModuleType;

                if (moduleType.Equals("IHM Survey", StringComparison.OrdinalIgnoreCase))
                    parsedModuleType = SurveyModuleType.IhmSurvey;
                else if (moduleType.Equals("EX Survey", StringComparison.OrdinalIgnoreCase))
                    parsedModuleType = SurveyModuleType.ExSurvey;
                else if (!Enum.TryParse(moduleType, true, out parsedModuleType))
                    return BadRequest("Invalid moduleType.");

                query = query.Where(x => x.ModuleType == parsedModuleType);
            }

            if (status.HasValue)
                query = query.Where(x => x.Status == status.Value);

            if (assignedSurveyorUserId.HasValue && assignedSurveyorUserId.Value > 0)
            {
                query = query.Where(x =>
                    x.Assignments.Any(a =>
                        a.SurveyorUserId == assignedSurveyorUserId.Value &&
                        a.Status != SurveyAssignmentStatus.Returned));
            }

            var jobs = await query
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            return Ok(await ToDtosAsync(jobs));
        }



        [HttpGet("summary")]
        public async Task<IActionResult> GetSurveySummary()
        {
            var total = await _db.SurveyJobs.CountAsync();
            var exSurvey = await _db.SurveyJobs.CountAsync(x => x.ModuleType == SurveyModuleType.ExSurvey);
            var ihmSurvey = await _db.SurveyJobs.CountAsync(x => x.ModuleType == SurveyModuleType.IhmSurvey);
            var submitted = await _db.SurveyJobs.CountAsync(x => x.Status == SurveyJobStatus.Submitted);
            var approved = await _db.SurveyJobs.CountAsync(x => x.Status == SurveyJobStatus.Approved);
            var returned = await _db.SurveyJobs.CountAsync(x => x.Status == SurveyJobStatus.QcReturned);

            return Ok(new SurveySummaryDto
            {
                TotalSurveyJobs = total,
                ExSurveyJobs = exSurvey,
                IhmSurveyJobs = ihmSurvey,
                SubmittedJobs = submitted,
                ApprovedJobs = approved,
                ReturnedJobs = returned
            });
        }


        [HttpPost("jobs/{id:int}/assign-surveyors")]
        public async Task<IActionResult> AssignSurveyors(int id, [FromBody] AssignSurveyorsRequest request)
        {
            var job = await _db.SurveyJobs
                .Include(x => x.Assignments)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (job == null)
                return NotFound("Survey job not found.");

            if (request.PrimarySurveyorUserId.HasValue &&
                request.SecondarySurveyorUserId.HasValue &&
                request.PrimarySurveyorUserId.Value == request.SecondarySurveyorUserId.Value)
            {
                return BadRequest("Primary and secondary surveyor cannot be same.");
            }

            var activeAssignments = job.Assignments
                .Where(x => x.Status != SurveyAssignmentStatus.Returned)
                .ToList();
            var isReassignment = activeAssignments.Count > 0;
            foreach (var oldAssignment in activeAssignments)
            {
                oldAssignment.Status = SurveyAssignmentStatus.Returned;
            }

            var assignedBy = request.AssignedByUserId ?? GetCurrentUserId() ?? 0;
            var assignedByName = await GetUserDisplayNameAsync(assignedBy);

            var surveyorIds = new[]
            {
        request.PrimarySurveyorUserId,
        request.SecondarySurveyorUserId
    }
            .Where(x => x.HasValue && x.Value > 0)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

            var surveyors = await _db.users
                .Where(x => surveyorIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id);
            var missingSurveyorIds = surveyorIds
                .Where(idValue => !surveyors.ContainsKey(idValue))
                .ToList();
            if (missingSurveyorIds.Count > 0)
                return BadRequest($"Surveyor not found: {string.Join(", ", missingSurveyorIds)}");

            var assignedAt = DateTime.UtcNow;
            var emailRecipients = new List<(User User, string Role)>();
            for (var index = 0; index < surveyorIds.Count; index++)
            {
                var role = index == 0 ? "Primary Surveyor" : "Secondary Surveyor";
                job.Assignments.Add(new SurveyAssignment
                {
                    SurveyJobId = job.Id,
                    SurveyorUserId = surveyorIds[index],
                    AssignedByUserId = assignedBy,
                    // Keep the requested primary/secondary order deterministic.
                    AssignedAt = assignedAt.AddTicks(index),
                    Status = SurveyAssignmentStatus.Assigned
                });
                _db.UserNotifications.Add(CreateAssignmentNotification(
                    surveyorIds[index],
                    job,
                    role,
                    assignedByName,
                    isReassignment));
                emailRecipients.Add((surveyors[surveyorIds[index]], role));
            }

            job.Status = surveyorIds.Any()
                ? SurveyJobStatus.Assigned
                : SurveyJobStatus.Draft;

            job.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            foreach (var recipient in emailRecipients)
            {
                await TrySendAssignmentEmailAsync(
                    recipient.User,
                    job,
                    recipient.Role,
                    assignedByName,
                    isReassignment);
            }

            return Ok(ToDto(job));
        }

        public class AssignSurveyorsRequest
        {
            public int? PrimarySurveyorUserId { get; set; }
            public int? SecondarySurveyorUserId { get; set; }
            public int? AssignedByUserId { get; set; }
        }





        private int? GetCurrentUserId()
        {
            var claimValue =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub") ??
                User.FindFirstValue("userId");

            return int.TryParse(claimValue, out var userId) ? userId : null;
        }

        private async Task<string> GetUserDisplayNameAsync(int userId)
        {
            if (userId <= 0)
                return "Administrator";

            return await _db.users
                .Where(x => x.Id == userId)
                .Select(x => x.FullName)
                .FirstOrDefaultAsync() ?? $"User {userId}";
        }

        private static UserNotification CreateAssignmentNotification(
            int userId,
            SurveyJob job,
            string role,
            string assignedByName,
            bool isReassignment)
        {
            var action = isReassignment ? "reassigned" : "assigned";
            var vessel = string.IsNullOrWhiteSpace(job.VesselName)
                ? "the selected vessel"
                : job.VesselName;

            return new UserNotification
            {
                UserId = userId,
                Type = "SurveyAssignment",
                Title = isReassignment
                    ? "Survey job reassigned"
                    : "New survey job assigned",
                Message =
                    $"You were {action} as {role} for job {job.JobNumber} ({vessel}) by {assignedByName}.",
                SurveyJobId = job.Id,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
        }

        private async Task TrySendAssignmentEmailAsync(
            User surveyor,
            SurveyJob job,
            string role,
            string assignedByName,
            bool isReassignment)
        {
            if (string.IsNullOrWhiteSpace(surveyor.Email))
                return;

            var action = isReassignment ? "reassigned" : "assigned";
            var subject = isReassignment
                ? $"Survey job reassigned: {job.JobNumber}"
                : $"New survey assignment: {job.JobNumber}";
            var startDate = job.StartDate?.ToString("dd MMM yyyy") ?? "Not specified";
            var endDate = job.EndDate?.ToString("dd MMM yyyy") ?? "Not specified";

            var body = $"""
                <div style="font-family:Arial,sans-serif;color:#172033;line-height:1.55">
                  <h2 style="color:#1769aa">Survey assignment</h2>
                  <p>Hello {WebUtility.HtmlEncode(surveyor.FullName)},</p>
                  <p>You have been <strong>{action}</strong> as
                     <strong>{WebUtility.HtmlEncode(role)}</strong>.</p>
                  <table style="border-collapse:collapse">
                    <tr><td style="padding:4px 16px 4px 0"><strong>Job</strong></td>
                        <td>{WebUtility.HtmlEncode(job.JobNumber)}</td></tr>
                    <tr><td style="padding:4px 16px 4px 0"><strong>Module</strong></td>
                        <td>{WebUtility.HtmlEncode(job.ModuleType.ToString())}</td></tr>
                    <tr><td style="padding:4px 16px 4px 0"><strong>Vessel</strong></td>
                        <td>{WebUtility.HtmlEncode(job.VesselName ?? "Not specified")}</td></tr>
                    <tr><td style="padding:4px 16px 4px 0"><strong>Project</strong></td>
                        <td>{WebUtility.HtmlEncode(job.ProjectName ?? "Not specified")}</td></tr>
                    <tr><td style="padding:4px 16px 4px 0"><strong>Schedule</strong></td>
                        <td>{startDate} to {endDate}</td></tr>
                    <tr><td style="padding:4px 16px 4px 0"><strong>Assigned by</strong></td>
                        <td>{WebUtility.HtmlEncode(assignedByName)}</td></tr>
                  </table>
                  <p>Please open the Auras app to review the assignment.</p>
                </div>
                """;

            try
            {
                await _emailService.SendEmailAsync(surveyor.Email, subject, body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Assignment email failed for user {UserId}, job {JobId}.",
                    surveyor.Id,
                    job.Id);
            }
        }

        private async Task<List<SurveyJobDto>> ToDtosAsync(List<SurveyJob> jobs)
        {
            var userIds = jobs
                .SelectMany(j => j.Assignments.Select(a => a.SurveyorUserId)
                    .Concat(j.Assignments.Select(a => a.AssignedByUserId)))
                .Distinct()
                .ToList();

            var userNames = await _db.users
    .Where(u => userIds.Contains(u.Id))
    .ToDictionaryAsync(
        u => u.Id,
        u => !string.IsNullOrWhiteSpace(u.FullName)
            ? u.FullName
            : (!string.IsNullOrWhiteSpace(u.Email) ? u.Email : $"User {u.Id}")
    );

            return jobs.Select(job => ToDto(job, userNames)).ToList();
        }

        private static SurveyJobDto ToDto(
            SurveyJob job,
            IReadOnlyDictionary<int, string>? userNames = null)
        {
            var assignments = job.Assignments
                .OrderByDescending(x => x.AssignedAt)
                .Select(x => new SurveyAssignmentDto
                {
                    SurveyorUserId = x.SurveyorUserId,
                    SurveyorName = userNames != null && userNames.TryGetValue(x.SurveyorUserId, out var surveyorName)
                        ? surveyorName
                        : $"User {x.SurveyorUserId}",
                    AssignedByUserId = x.AssignedByUserId,
                    AssignedByName = userNames != null && userNames.ContainsKey(x.AssignedByUserId)
                        ? userNames[x.AssignedByUserId]
                        : $"User {x.AssignedByUserId}",
                    AssignedAt = x.AssignedAt,
                    Status = x.Status.ToString()

                })
                .ToList();

            var latestAssignment = assignments
                .FirstOrDefault(x => x.Status != "Returned") ?? assignments.FirstOrDefault();
            var activeAssignments = assignments
                .Where(x => x.Status != "Returned")
                .OrderBy(x => x.AssignedAt)
                .ToList();

            return new SurveyJobDto
            {
                Id = job.Id,
                ModuleType = job.ModuleType.ToString(),
                JobNumber = job.JobNumber,
                ClientName = job.ClientName,
                ProjectName = job.ProjectName,
                VesselId = job.VesselId,
                VesselSnapshotJson = job.VesselSnapshotJson,
                VesselName = job.VesselName,
                ImoNumber = job.ImoNumber,
                SiteName = job.SiteName,
                StartDate = job.StartDate,
                SurveyDate = job.SurveyDate,
                EndDate = job.EndDate,
                Status = job.Status.ToString(),
                CreatedByUserId = job.CreatedByUserId,
                CreatedAt = job.CreatedAt,
                UpdatedAt = job.UpdatedAt,
                AuditPort = job.AuditPort,
                AuditAddress = job.AuditAddress,

                AssignedSurveyorIds = assignments
                    .Select(x => x.SurveyorUserId)
                    .Distinct()
                    .ToList(),

                AssignedSurveyorName = latestAssignment?.SurveyorName,
                AssignedAt = latestAssignment?.AssignedAt,
                PrimarySurveyorUserId = activeAssignments.ElementAtOrDefault(0)?.SurveyorUserId,
                SecondarySurveyorUserId = activeAssignments.ElementAtOrDefault(1)?.SurveyorUserId,
                Assignments = assignments
            };
        }

        public class CreateSurveyJobRequest
        {
            public SurveyModuleType ModuleType { get; set; }

            public string JobNumber { get; set; } = string.Empty;
            public int? VesselId { get; set; }
            public string? ClientName { get; set; }

            public string? ProjectName { get; set; }

            public string? VesselName { get; set; }

            public string? ImoNumber { get; set; }

            public string? SiteName { get; set; }

            public DateTime? StartDate { get; set; }

            public DateTime? EndDate { get; set; }

            public string? AuditPort { get; set; }
            public string? AuditAddress { get; set; }


            public int? CreatedByUserId { get; set; }
        }

        public class UpdateSurveyJobRequest
        {
            public string? ClientName { get; set; }

            public string? ProjectName { get; set; }

            public string? VesselName { get; set; }

            public string? ImoNumber { get; set; }

            public string? SiteName { get; set; }

            public string? AuditPort { get; set; }
            public string? AuditAddress { get; set; }


            public DateTime? StartDate { get; set; }

            public DateTime? EndDate { get; set; }
        }

        public class AssignSurveyJobRequest
        {
            public int SurveyorUserId { get; set; }

            public int? AssignedByUserId { get; set; }
        }
        public class UpdateSurveyDateRequest
        {
            public DateTime? SurveyDate { get; set; }
        }
        public class UpdateSurveyJobStatusRequest
        {
            public SurveyJobStatus Status { get; set; }

            public DateTime? SurveyDate { get; set; }
        }

        public class SurveyJobDto
        {
            public int Id { get; set; }

            public string ModuleType { get; set; } = string.Empty;

            public string JobNumber { get; set; } = string.Empty;

            public string? ClientName { get; set; }

            public string? AuditPort { get; set; }
            public string? AuditAddress { get; set; }


            public string? ProjectName { get; set; }
            public int? VesselId { get; set; }
            public string? VesselSnapshotJson { get; set; }
            public string? VesselName { get; set; }

            public string? ImoNumber { get; set; }

            public string? SiteName { get; set; }

            public DateTime? StartDate { get; set; }

            public DateTime? SurveyDate { get; set; }

            public DateTime? EndDate { get; set; }

            public string Status { get; set; } = string.Empty;

            public int CreatedByUserId { get; set; }

            public DateTime CreatedAt { get; set; }

            public DateTime? UpdatedAt { get; set; }

            public List<int> AssignedSurveyorIds { get; set; } = new();

            public string? AssignedSurveyorName { get; set; }

            public DateTime? AssignedAt { get; set; }

            public int? PrimarySurveyorUserId { get; set; }

            public int? SecondarySurveyorUserId { get; set; }

            public List<SurveyAssignmentDto> Assignments { get; set; } = new();

        }

        public class SurveyAssignmentDto
        {
            public int SurveyorUserId { get; set; }

            public string SurveyorName { get; set; } = string.Empty;

            public int AssignedByUserId { get; set; }

            public string AssignedByName { get; set; } = string.Empty;

            public DateTime AssignedAt { get; set; }

            public string Status { get; set; } = string.Empty;
        }

        public class SurveyPhotoDto
        {
            public int Id { get; set; }

            public int SurveyJobId { get; set; }

            public string? RelatedModule { get; set; }

            public int? RelatedRecordId { get; set; }

            public string OriginalFileName { get; set; } = string.Empty;

            public string FilePath { get; set; } = string.Empty;

            public string? ContentType { get; set; }

            public int UploadedByUserId { get; set; }

            public DateTime UploadedAt { get; set; }
        }
        public class SurveySummaryDto
        {
            public int TotalSurveyJobs { get; set; }

            public int ExSurveyJobs { get; set; }

            public int IhmSurveyJobs { get; set; }

            public int SubmittedJobs { get; set; }

            public int ApprovedJobs { get; set; }

            public int ReturnedJobs { get; set; }
        }

    }
}
