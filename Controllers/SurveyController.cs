using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Survey_Certification_App.Data;
using Survey_Certification_App.IemailserviceInterface;
using Survey_Certification_App.Models;
using Survey_Certification_App.Models.Survey;
using Survey_Certification_App.Services;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;

namespace Survey_Certification_App.Controllers
{
    [Route("api/survey")]
    [ApiController]
    public class SurveyController : ControllerBase
    {
        private const string VscpChecklistFileType = "VscpChecklistTemplate";
        private readonly ApplicationDbContext _db;
        private readonly IEmailService _emailService;
        private readonly ILogger<SurveyController> _logger;
        private readonly AccessScopeService _access;
        public SurveyController(
            ApplicationDbContext db,
            IEmailService emailService,
            ILogger<SurveyController> logger,
            AccessScopeService access)
        {
            _db = db;
            _emailService = emailService;
            _logger = logger;
            _access = access;
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
                .Include(x => x.UploadedFiles)
                .Where(x =>
                    x.AssignedUserId == currentUserId ||
                    x.Assignments.Any(a =>
                        a.SurveyorUserId == currentUserId &&
                        a.Status != SurveyAssignmentStatus.Returned))
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

            if (!await CanAccessJobAsync(id))
                return Forbid();

            var userIds = job.Assignments
            .Select(a => a.SurveyorUserId)
            .Concat(job.Assignments.Select(a => a.AssignedByUserId))
            .Concat(new[] { job.CreatedByUserId })
            .Concat(job.AssignedManagerId.HasValue ? new[] { job.AssignedManagerId.Value } : Array.Empty<int>())
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

            return Ok(await ToDtoAsync(job, userNames));
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

            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Unauthorized("Login is required to create a survey job.");

            var createdBy = currentUserId.Value;
            if (_access.IsManager && (!request.AssignedManagerId.HasValue || request.AssignedManagerId.Value <= 0))
            {
                request.AssignedManagerId = createdBy;
            }

            if (request.AssignedManagerId.HasValue && request.AssignedManagerId.Value > 0)
            {
                if (!await IsValidAssignedManagerAsync(request.AssignedManagerId.Value, request.ModuleType))
                    return BadRequest("Selected manager must be active, approved, and have Manager access for this project.");
            }


            Vessel? vessel = null;
            if (request.VesselId.HasValue && request.VesselId.Value > 0)
            {
                vessel = await _db.Vessels.FirstOrDefaultAsync(x => x.Id == request.VesselId.Value);

                if (vessel == null)
                    return BadRequest("Selected vessel not found.");
            }
            int? projectId;
            string? projectName;

            if (_access.IsAdmin)
            {
                if (!request.ProjectId.HasValue || request.ProjectId.Value <= 0)
                    return BadRequest(new { message = "Project is required when Admin creates a job." });

                var project = await _db.Projects
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == request.ProjectId.Value);

                if (project == null)
                    return BadRequest(new { message = "Invalid project selected." });

                projectId = project.Id;
                projectName = project.Name;
            }
            else
            {
                if (!_access.ProjectId.HasValue)
                    return Forbid();

                projectId = _access.ProjectId.Value;
                projectName = User.FindFirst("projectName")?.Value;
            }
            var job = new SurveyJob
            {
                ModuleType = request.ModuleType,
                ProjectId = projectId,
                ProjectName = projectName,
                JobNumber = request.JobNumber.Trim(),
                ClientName = vessel?.ClientName ?? request.ClientName,

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
                AssignedManagerId = request.AssignedManagerId,
                CreatedAt = DateTime.UtcNow
            };

            _db.SurveyJobs.Add(job);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetJobById), new { id = job.Id }, await ToDtoAsync(job));
        }


        [HttpPut("jobs/{id:int}")]
        public async Task<IActionResult> UpdateJob(int id, [FromBody] UpdateSurveyJobRequest request)
        {
            var job = await _db.SurveyJobs.FindAsync(id);

            if (job == null)
                return NotFound("Survey job not found.");

            if (!await CanEditJobAsync(job))
                return Forbid();

            if (request.AssignedManagerId.HasValue && request.AssignedManagerId.Value > 0)
            {
                if (!await IsValidAssignedManagerAsync(request.AssignedManagerId.Value, job.ModuleType))
                    return BadRequest("Selected manager must be active, approved, and have Manager access for this project.");
            }

            Vessel? vessel = null;
            if (request.VesselId.HasValue && request.VesselId.Value > 0)
            {
                vessel = await _db.Vessels.FirstOrDefaultAsync(x => x.Id == request.VesselId.Value);
                if (vessel == null)
                    return BadRequest("Selected vessel not found.");
            }

            job.ClientName = vessel?.ClientName ?? request.ClientName;
            if (IsCurrentUserAdmin() && !string.IsNullOrWhiteSpace(request.ProjectName))
                job.ProjectName = request.ProjectName;
            job.VesselId = vessel?.Id ?? request.VesselId;
            job.VesselName = vessel?.VesselName ?? request.VesselName;
            job.ImoNumber = vessel?.ImoNumber ?? request.ImoNumber;
            job.VesselSnapshotJson = vessel == null ? job.VesselSnapshotJson : System.Text.Json.JsonSerializer.Serialize(new
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
            });
            job.SiteName = request.SiteName;
            job.StartDate = request.StartDate;
            job.SurveyDate = request.SurveyDate;
            job.EndDate = request.EndDate;
            job.AuditPort = request.AuditPort;
            job.AuditAddress = request.AuditAddress;
            job.AssignedManagerId = request.AssignedManagerId;
            job.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(await ToDtoAsync(job));
        }

        [HttpPost("jobs/{id:int}/assign")]
        public async Task<IActionResult> AssignJob(int id, [FromBody] AssignSurveyJobRequest request)
        {
            var job = await _db.SurveyJobs
                .Include(x => x.Assignments)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (job == null)
                return NotFound("Survey job not found.");

            if (!await CanEditJobAsync(job))
                return Forbid();

            if (request.SurveyorUserId <= 0)
                return BadRequest("Surveyor user id is required.");

            var surveyorExists = await _db.users.AnyAsync(x => x.Id == request.SurveyorUserId);
            if (!surveyorExists)
                return BadRequest("Selected surveyor not found.");

            var assignedBy = request.AssignedByUserId ?? GetCurrentUserId() ?? 0;
            var assignedByName = await GetUserDisplayNameAsync(assignedBy);
            var activeAssignments = GetActiveAssignmentsInRoleOrder(job);
            var targets = activeAssignments
                .Select((assignment, index) =>
                    new SurveyorAssignmentTarget(
                        assignment.SurveyorUserId,
                        RoleForAssignmentIndex(index)))
                .ToList();

            if (!targets.Any(x => x.SurveyorUserId == request.SurveyorUserId))
            {
                targets.Add(new SurveyorAssignmentTarget(
                    request.SurveyorUserId,
                    RoleForAssignmentIndex(targets.Count)));
            }

            var surveyorIds = targets
                .Select(x => x.SurveyorUserId)
                .Distinct()
                .ToList();
            var surveyors = await _db.users
                .Where(x => surveyorIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id);

            var emailRecipients = ApplySurveyorAssignmentChanges(
                job,
                targets,
                surveyors,
                assignedBy,
                assignedByName);
            job.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await SendAssignmentChangeEmailsAsync(emailRecipients, job, assignedByName);

            return Ok(await ToDtoAsync(job));
        }
        [HttpPost("jobs/{id:int}/survey-date")]
        public async Task<IActionResult> UpdateSurveyDate(int id, [FromBody] UpdateSurveyDateRequest request)
        {
            var job = await _db.SurveyJobs.FindAsync(id);

            if (job == null)
                return NotFound("Survey job not found.");

            if (!await CanUpdateSurveyDateAsync(job))
                return Forbid();

            if (request.SurveyDate == null)
                return BadRequest("Survey date is required.");

            job.SurveyDate = request.SurveyDate;
            job.Status = SurveyJobStatus.InProgress;
            job.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(await ToDtoAsync(job));
        }
        [HttpPost("jobs/{id:int}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateSurveyJobStatusRequest request)
        {
            var job = await _db.SurveyJobs.FindAsync(id);

            if (job == null)
                return NotFound("Survey job not found.");

            if (!await CanEditJobAsync(job))
                return Forbid();

            job.Status = request.Status;

            if (request.Status == SurveyJobStatus.InProgress && job.SurveyDate == null)
            {
                job.SurveyDate = request.SurveyDate ?? DateTime.Today;
            }

            job.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(await ToDtoAsync(job));
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
            if (!await CanAccessJobAsync(id))
                return Forbid();

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

            if (!await CanEditJobAsync(job))
                return Forbid();

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
                _db.PdfDynamicEdits.RemoveRange(
                    await _db.PdfDynamicEdits.Where(x =>
                        (x.ReportType == "ihm" || x.ReportType == "exsurvey") &&
                        x.ReportId == id).ToListAsync());
                _db.ReportAssignments.RemoveRange(
                    await _db.ReportAssignments.Where(x =>
                        x.SurveyJobId == id ||
                        ((x.ReportType == "ihm" || x.ReportType == "exsurvey") && x.ReportId == id)).ToListAsync());
                _db.IhmMarkedImageMetadata.RemoveRange(
                    await _db.IhmMarkedImageMetadata.Where(x => x.SurveyJobId == id).ToListAsync());

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
         [FromQuery] int? assignedSurveyorUserId,
         [FromQuery] string? ihmCompletionFilter,
         [FromQuery] string? jobCompletionFilter)
        {
            var timer = Stopwatch.StartNew();
            var query = _db.SurveyJobs
                .AsNoTracking()
                .Include(x => x.Assignments)
                .Include(x => x.UploadedFiles)
                .AsQueryable();

            query = ApplyJobAccessFilter(query);

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
                    x.AssignedUserId == assignedSurveyorUserId.Value ||
                    x.Assignments.Any(a =>
                        a.SurveyorUserId == assignedSurveyorUserId.Value &&
                        a.Status != SurveyAssignmentStatus.Returned));
            }

            if (!string.IsNullOrWhiteSpace(ihmCompletionFilter))
            {
                if (!string.Equals(ihmCompletionFilter, "live", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(ihmCompletionFilter, "completed", StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Invalid ihmCompletionFilter. Use live or completed.");

                var candidateIds = await query
                    .Where(x => x.ModuleType == SurveyModuleType.IhmSurvey)
                    .Select(x => x.Id)
                    .Distinct()
                    .ToListAsync();

                var classifications = await BuildIhmAuditClassificationsAsync(candidateIds);
                var matchingIds = classifications
                    .Where(x => string.Equals(ihmCompletionFilter, "completed", StringComparison.OrdinalIgnoreCase)
                        ? x.IsCompleted
                        : !x.IsCompleted)
                    .Select(x => x.JobId)
                    .ToHashSet();

                query = query.Where(x => matchingIds.Contains(x.Id));
            }

            if (!string.IsNullOrWhiteSpace(jobCompletionFilter))
            {
                if (!string.Equals(jobCompletionFilter, "live", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(jobCompletionFilter, "completed", StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Invalid jobCompletionFilter. Use live or completed.");

                query = string.Equals(jobCompletionFilter, "completed", StringComparison.OrdinalIgnoreCase)
                    ? query.Where(x => CompletedSurveyStatuses.Contains(x.Status))
                    : query.Where(x => !CompletedSurveyStatuses.Contains(x.Status));
            }

            var jobs = await query
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            var result = await ToDtosAsync(jobs);
            timer.Stop();
            _logger.LogInformation(
                "Survey jobs API loaded. ModuleType={ModuleType}, Status={Status}, AssignedSurveyorUserId={AssignedSurveyorUserId}, IhmCompletionFilter={IhmCompletionFilter}, JobCompletionFilter={JobCompletionFilter}, Jobs={JobCount}, ElapsedMs={ElapsedMs}",
                moduleType,
                status,
                assignedSurveyorUserId,
                ihmCompletionFilter,
                jobCompletionFilter,
                result.Count,
                timer.ElapsedMilliseconds);

            return Ok(result);
        }
        private static readonly SurveyJobStatus[] CompletedSurveyStatuses =
        {
    SurveyJobStatus.Closed,
    SurveyJobStatus.ReportGenerated
};




        [HttpGet("summary")]
        public async Task<IActionResult> GetSurveySummary([FromQuery] string period = "week")
        {
            var now = DateTime.UtcNow;
            var today = now.Date;

            var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
            var startOfMonth = new DateTime(today.Year, today.Month, 1);
            var startOfYear = new DateTime(today.Year, 1, 1);


            const int IHM_PROJECT_ID = 1;
            const int EX_SURVEY_PROJECT_ID = 2;

            var ihmUsersCount = await _db.users.CountAsync(x =>
                x.IsApproved &&
                x.ProjectId == IHM_PROJECT_ID);

            var exSurveyUsersCount = await _db.users.CountAsync(x =>
                x.IsApproved &&
                x.ProjectId == EX_SURVEY_PROJECT_ID);
            // Scope to what the current user (Admin/Manager/Surveyor) can actually see
            var scopedJobs = ApplyJobAccessFilter(_db.SurveyJobs.AsNoTracking());

            var activeJobs = scopedJobs
                .Where(x =>
                    x.Status != SurveyJobStatus.Draft &&
                    x.Status != SurveyJobStatus.QcReturned);

            var completedJobs = activeJobs
                .Where(x => CompletedSurveyStatuses.Contains(x.Status));

            var completedJobDates = await completedJobs
                .Select(x => new
                {
                    x.Id,
                    CompletedAt = x.UpdatedAt ?? x.EndDate ?? x.SurveyDate ?? x.CreatedAt
                })
                .ToListAsync();

            int CountCompleted(DateTime? from, DateTime? to = null)
            {
                var query = completedJobDates.AsEnumerable();

                if (from.HasValue)
                    query = query.Where(x => x.CompletedAt >= from.Value);

                if (to.HasValue)
                    query = query.Where(x => x.CompletedAt < to.Value);

                return query
                    .Select(x => x.Id)
                    .Distinct()
                    .Count();
            }

            var total = await scopedJobs.CountAsync();
            var exSurvey = await scopedJobs.CountAsync(x => x.ModuleType == SurveyModuleType.ExSurvey);
            var ihmSurvey = await scopedJobs.CountAsync(x => x.ModuleType == SurveyModuleType.IhmSurvey);
            var submitted = await activeJobs.CountAsync(x => x.Status == SurveyJobStatus.Submitted);
            var approved = await activeJobs.CountAsync(x => x.Status == SurveyJobStatus.Approved);



            // Was querying the whole table unscoped before — now respects the same access filter
            var returned = await scopedJobs.CountAsync(x => x.Status == SurveyJobStatus.QcReturned);
            var attention = await scopedJobs.CountAsync(x =>
    x.Status == SurveyJobStatus.Assigned &&
    x.SurveyDate == null);
            var ihmJobIds = await scopedJobs
                .Where(x => x.ModuleType == SurveyModuleType.IhmSurvey)
                .Select(x => x.Id)
                .Distinct()
                .ToListAsync();
            var ihmClassifications = await BuildIhmAuditClassificationsAsync(ihmJobIds);
            var liveIhmJobs = ihmClassifications.Count(x => !x.IsCompleted);
            var completedIhmJobs = ihmClassifications.Count(x => x.IsCompleted);
            var exSurveyLiveJobs = await activeJobs.CountAsync(x =>
                x.ModuleType == SurveyModuleType.ExSurvey &&
                !CompletedSurveyStatuses.Contains(x.Status));
            var exSurveyReports = await activeJobs.CountAsync(x =>
                x.ModuleType == SurveyModuleType.ExSurvey &&
                (x.Status == SurveyJobStatus.ReportGenerated ||
                 x.Status == SurveyJobStatus.Approved ||
                 x.Status == SurveyJobStatus.Closed ||
                 x.Status == SurveyJobStatus.Submitted));
            var trend = new List<SurveyCompletionTrendPointDto>();
            var trendMode = string.Equals(period, "month", StringComparison.OrdinalIgnoreCase)
                ? "month"
                : "week";

            if (trendMode == "month")
            {
                for (var i = 5; i >= 0; i--)
                {
                    var monthStart = new DateTime(today.Year, today.Month, 1).AddMonths(-i);
                    var monthEnd = monthStart.AddMonths(1);

                    trend.Add(new SurveyCompletionTrendPointDto
                    {
                        Label = monthStart.ToString("MMM"),
                        Count = CountCompleted(monthStart, monthEnd)
                    });
                }
            }
            else
            {
                for (var i = 5; i >= 0; i--)
                {
                    var weekStart = startOfWeek.AddDays(-7 * i);
                    var weekEnd = weekStart.AddDays(7);

                    trend.Add(new SurveyCompletionTrendPointDto
                    {
                        Label = $"W{System.Globalization.ISOWeek.GetWeekOfYear(weekStart)}",
                        Count = CountCompleted(weekStart, weekEnd)
                    });
                }
            }

            return Ok(new SurveySummaryDto
            {
                TotalSurveyJobs = total,
                ExSurveyJobs = exSurvey,
                IhmSurveyJobs = ihmSurvey,
                SubmittedJobs = submitted,
                ApprovedJobs = approved,
                ReturnedJobs = returned,
                AttentionJobs = attention,
                LiveIhmJobs = liveIhmJobs,
                IhmLiveJobs = liveIhmJobs,
                IhmCompletedJobs = completedIhmJobs,
                ExSurveyLiveJobs = exSurveyLiveJobs,
                ExSurveyReports = exSurveyReports,
                IhmUsersCount = ihmUsersCount,
                ExSurveyUsersCount = exSurveyUsersCount,
                ThisWeekCompletedJobs = CountCompleted(startOfWeek),
                ThisMonthCompletedJobs = CountCompleted(startOfMonth),
                ThisYearCompletedJobs = CountCompleted(startOfYear),
                OverallCompletedJobs = CountCompleted(null),
                CompletionTrend = trend
            });


        }

        [Authorize]
        [HttpGet("dashboard/my-summary")]
        public async Task<IActionResult> GetMySurveyorDashboardSummary()
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue || currentUserId.Value <= 0)
                return Unauthorized(new { message = "User identity was not found." });

            if (!string.Equals(GetCurrentUserRole(), "Surveyor", StringComparison.OrdinalIgnoreCase))
                return Forbid();

            var myJobs = _db.SurveyJobs
                .AsNoTracking()
                .Where(x =>
                    x.ModuleType == SurveyModuleType.IhmSurvey &&
                    (x.AssignedUserId == currentUserId.Value ||
                     x.Assignments.Any(a =>
                         a.SurveyorUserId == currentUserId.Value &&
                         a.Status != SurveyAssignmentStatus.Returned)));

            var myJobIds = await myJobs
                .Select(x => x.Id)
                .Distinct()
                .ToListAsync();
            var myClassifications = await BuildIhmAuditClassificationsAsync(myJobIds);
            var liveJobs = myClassifications.Count(x => !x.IsCompleted);
            var completedJobs = myClassifications.Count(x => x.IsCompleted);
            var completedJobIds = myClassifications
                .Where(x => x.IsCompleted)
                .Select(x => x.JobId)
                .ToHashSet();
            var today = DateTime.Today;
            var myJobDueData = await myJobs
                .Select(x => new
                {
                    x.Id,
                    x.EndDate,
                    x.Status
                })
                .ToListAsync();
            var dueTodayJobs = myJobDueData.Count(x =>
                x.EndDate.HasValue &&
                x.EndDate.Value.Date == today &&
                !completedJobIds.Contains(x.Id) &&
                !CompletedSurveyStatuses.Contains(x.Status));

            return Ok(new SurveyorDashboardSummaryDto
            {
                LiveJobs = liveJobs,
                CompletedJobs = completedJobs,
                DueTodayJobs = dueTodayJobs
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

            if (!await CanEditJobAsync(job))
                return Forbid();

            if (request.PrimarySurveyorUserId.HasValue &&
                request.SecondarySurveyorUserId.HasValue &&
                request.PrimarySurveyorUserId.Value == request.SecondarySurveyorUserId.Value)
            {
                return BadRequest("Primary and secondary surveyor cannot be same.");
            }

            var assignedBy = request.AssignedByUserId ?? GetCurrentUserId() ?? 0;
            var assignedByName = await GetUserDisplayNameAsync(assignedBy);

            var targets = new[]
            {
                request.PrimarySurveyorUserId,
                request.SecondarySurveyorUserId
            }
                .Select((idValue, index) => new { idValue, index })
                .Where(x => x.idValue.HasValue && x.idValue.Value > 0)
                .Select(x => new SurveyorAssignmentTarget(
                    x.idValue!.Value,
                    RoleForAssignmentIndex(x.index)))
                .ToList();

            var surveyorIds = targets
                .Select(x => x.SurveyorUserId)
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

            var emailRecipients = ApplySurveyorAssignmentChanges(
                job,
                targets,
                surveyors,
                assignedBy,
                assignedByName);
            job.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await SendAssignmentChangeEmailsAsync(emailRecipients, job, assignedByName);

            return Ok(await ToDtoAsync(job));
        }

        [HttpPost("jobs/{id:int}/assigned-manager")]
        public async Task<IActionResult> UpdateAssignedManager(
            int id,
            [FromBody] UpdateAssignedManagerRequest request)
        {
            var job = await _db.SurveyJobs
                .Include(x => x.Assignments)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (job == null)
                return NotFound("Survey job not found.");

            if (!await CanEditJobAsync(job))
                return Forbid();

            if (request.AssignedManagerId.HasValue && request.AssignedManagerId.Value > 0)
            {
                if (!await IsValidAssignedManagerAsync(request.AssignedManagerId.Value, job.ModuleType))
                    return BadRequest("Selected manager must be active, approved, and have Manager access for this project.");
            }

            job.AssignedManagerId = request.AssignedManagerId;
            job.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return Ok(await ToDtoAsync(job));
        }

        public class AssignSurveyorsRequest
        {
            public int? PrimarySurveyorUserId { get; set; }
            public int? SecondarySurveyorUserId { get; set; }
            public int? AssignedByUserId { get; set; }
        }

        public class UpdateAssignedManagerRequest
        {
            public int? AssignedManagerId { get; set; }
        }





        private int? GetCurrentUserId()
        {
            var claimValue =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub") ??
                User.FindFirstValue("userId");

            return int.TryParse(claimValue, out var userId) ? userId : null;
        }

        private string GetCurrentUserRole()
        {
            return User.FindFirstValue(ClaimTypes.Role) ??
                User.FindFirstValue("role") ??
                string.Empty;
        }

        private bool IsCurrentUserAdmin() =>
            string.Equals(GetCurrentUserRole(), "Admin", StringComparison.OrdinalIgnoreCase);

        private IQueryable<SurveyJob> ApplyJobAccessFilter(IQueryable<SurveyJob> query)
        {
            return _access.FilterJobs(query);
        }

        private async Task<List<IhmAuditJobClassification>> BuildIhmAuditClassificationsAsync(
            IEnumerable<int> jobIds,
            bool logDetails = false)
        {
            var ids = jobIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                return new List<IhmAuditJobClassification>();

            var stats = await _db.IhmCheckpoints
                .AsNoTracking()
                .Where(x => ids.Contains(x.SurveyJobId))
                .GroupBy(x => x.SurveyJobId)
                .Select(group => new
                {
                    JobId = group.Key,
                    Total = group.Count(),
                    Audited = group.Count(x => x.Result != IhmCheckpointResult.Pending)
                })
                .ToListAsync();

            var byJob = stats.ToDictionary(x => x.JobId);
            var result = new List<IhmAuditJobClassification>(ids.Count);

            foreach (var jobId in ids)
            {
                var total = byJob.TryGetValue(jobId, out var stat) ? stat.Total : 0;
                var audited = byJob.TryGetValue(jobId, out stat) ? stat.Audited : 0;
                var pending = Math.Max(total - audited, 0);
                var percentage = total <= 0
                    ? 0m
                    : Math.Clamp((decimal)audited * 100m / total, 0m, 100m);
                var isCompleted = total > 0 && pending == 0;

                if (logDetails)
                {
                    _logger.LogInformation(
                        "IHM area survey classification. JobId={JobId}, TotalAuditItems={Total}, CompletedAuditItems={Audited}, PendingAuditItems={Pending}, Percentage={Percentage}, Classification={Classification}",
                        jobId,
                        total,
                        audited,
                        pending,
                        percentage,
                        isCompleted ? "Completed" : "Live");
                }

                result.Add(new IhmAuditJobClassification(
                    jobId,
                    total,
                    audited,
                    pending,
                    percentage,
                    isCompleted));
            }

            return result;
        }

        private async Task<bool> CanAccessJobAsync(int jobId)
        {
            return await ApplyJobAccessFilter(_db.SurveyJobs.AsQueryable())
                .AnyAsync(x => x.Id == jobId);
        }
        private async Task<bool> CanUpdateSurveyDateAsync(SurveyJob job)
        {
            if (!_access.UserId.HasValue)
                return false;

            if (_access.IsAdmin)
                return true;

            if (_access.IsManager)
                return job.CreatedByUserId == _access.UserId.Value;

            var userId = _access.UserId.Value;

            if (!string.Equals(GetCurrentUserRole(), "Surveyor", StringComparison.OrdinalIgnoreCase))
                return false;

            if (job.AssignedUserId == userId)
                return true;

            return await _db.SurveyAssignments
                .AsNoTracking()
                .AnyAsync(x =>
                    x.SurveyJobId == job.Id &&
                    x.SurveyorUserId == userId &&
                    x.Status != SurveyAssignmentStatus.Returned);
        }
        private Task<bool> CanEditJobAsync(SurveyJob job)
        {
            if (_access.IsAdmin)
                return Task.FromResult(true);

            if (!_access.IsManager || !_access.UserId.HasValue)
                return Task.FromResult(false);

            return Task.FromResult(job.CreatedByUserId == _access.UserId.Value);
        }

        private bool CanEditJob(SurveyJob job)
        {
            if (_access.IsAdmin)
                return true;

            return _access.IsManager &&
                   _access.UserId.HasValue &&
                   job.CreatedByUserId == _access.UserId.Value;
        }

        private async Task<bool> IsValidAssignedManagerAsync(int managerId, SurveyModuleType moduleType)
        {
            var userIsApproved = await _db.users.AsNoTracking().AnyAsync(x =>
                x.Id == managerId &&
                x.IsApproved &&
                !x.IsRejected);

            if (!userIsApproved)
                return false;

            var projectName = moduleType == SurveyModuleType.IhmSurvey
                ? "IHM"
                : "EX Survey";

            return await _access.HasProjectRoleAsync(managerId, projectName, "Manager");
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

        private static List<SurveyAssignment> GetActiveAssignmentsInRoleOrder(SurveyJob job)
        {
            return job.Assignments
                .Where(x => x.Status != SurveyAssignmentStatus.Returned)
                .OrderBy(x => x.AssignedAt)
                .ThenBy(x => x.Id)
                .ToList();
        }

        private static string RoleForAssignmentIndex(int index)
        {
            return index switch
            {
                0 => "Primary Surveyor",
                1 => "Secondary Surveyor",
                _ => "Additional Surveyor"
            };
        }

        private List<SurveyorAssignmentEmailRecipient> ApplySurveyorAssignmentChanges(
            SurveyJob job,
            IReadOnlyList<SurveyorAssignmentTarget> targets,
            IReadOnlyDictionary<int, User> surveyors,
            int assignedBy,
            string assignedByName)
        {
            var activeAssignments = GetActiveAssignmentsInRoleOrder(job);
            var retainedAssignments = new HashSet<SurveyAssignment>();
            var emailRecipients = new List<SurveyorAssignmentEmailRecipient>();
            var assignedAt = DateTime.UtcNow;

            for (var index = 0; index < targets.Count; index++)
            {
                var target = targets[index];
                var currentAssignment = activeAssignments.ElementAtOrDefault(index);

                if (currentAssignment?.SurveyorUserId == target.SurveyorUserId)
                {
                    retainedAssignments.Add(currentAssignment);
                    continue;
                }

                var isReassignment = currentAssignment != null;
                var newAssignment = new SurveyAssignment
                {
                    SurveyJobId = job.Id,
                    SurveyorUserId = target.SurveyorUserId,
                    AssignedByUserId = assignedBy,
                    AssignedAt = assignedAt.AddTicks(index),
                    Status = SurveyAssignmentStatus.Assigned
                };

                job.Assignments.Add(newAssignment);

                _db.UserNotifications.Add(CreateAssignmentNotification(
                    target.SurveyorUserId,
                    job,
                    target.Role,
                    assignedByName,
                    isReassignment));

                if (surveyors.TryGetValue(target.SurveyorUserId, out var surveyor))
                {
                    emailRecipients.Add(new SurveyorAssignmentEmailRecipient(
                        surveyor,
                        target.Role,
                        isReassignment));
                }
            }

            foreach (var activeAssignment in activeAssignments)
            {
                if (!retainedAssignments.Contains(activeAssignment))
                {
                    activeAssignment.Status = SurveyAssignmentStatus.Returned;
                }
            }

            job.Status = targets.Count > 0
                ? SurveyJobStatus.Assigned
                : SurveyJobStatus.Draft;
            job.AssignedUserId = targets.Count > 0
                ? targets[0].SurveyorUserId
                : null;

            return emailRecipients;
        }

        private async Task SendAssignmentChangeEmailsAsync(
            IEnumerable<SurveyorAssignmentEmailRecipient> recipients,
            SurveyJob job,
            string assignedByName)
        {
            foreach (var recipient in recipients)
            {
                await TrySendAssignmentEmailAsync(
                    recipient.User,
                    job,
                    recipient.Role,
                    assignedByName,
                    recipient.IsReassignment);
            }
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
                .Concat(jobs.Select(j => j.CreatedByUserId))
                .Concat(jobs.Where(j => j.AssignedUserId.HasValue).Select(j => j.AssignedUserId!.Value))
                .Concat(jobs.Where(j => j.AssignedManagerId.HasValue).Select(j => j.AssignedManagerId!.Value))
                .Distinct()
                .ToList();

            var userNames = userIds.Count == 0
                ? new Dictionary<int, string>()
                : await _db.users
                    .AsNoTracking()
                    .Where(u => userIds.Contains(u.Id))
                    .ToDictionaryAsync(
                        u => u.Id,
                        u => !string.IsNullOrWhiteSpace(u.FullName)
                            ? u.FullName
                            : (!string.IsNullOrWhiteSpace(u.Email) ? u.Email : $"User {u.Id}"));

            Dictionary<int, IhmSurveyProgress> progressByJobId;
            try
            {
                progressByJobId = await GetIhmProgressByJobIdAsync(jobs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to calculate IHM survey progress for job list. Returning jobs without progress.");
                progressByJobId = new Dictionary<int, IhmSurveyProgress>();
            }

            return jobs
                .Select(job => ToDto(job, userNames, GetProgressForJob(job, progressByJobId), CanEditJob(job)))
                .ToList();
        }

        private async Task<SurveyJobDto> ToDtoAsync(
            SurveyJob job,
            IReadOnlyDictionary<int, string>? userNames = null)
        {
            Dictionary<int, IhmSurveyProgress> progressByJobId;
            try
            {
                progressByJobId = await GetIhmProgressByJobIdAsync(new[] { job });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to calculate IHM survey progress for job {JobId}. Returning job without progress.", job.Id);
                progressByJobId = new Dictionary<int, IhmSurveyProgress>();
            }
            return ToDto(job, userNames, GetProgressForJob(job, progressByJobId), CanEditJob(job));
        }

        private async Task<Dictionary<int, IhmSurveyProgress>> GetIhmProgressByJobIdAsync(
            IEnumerable<SurveyJob> jobs)
        {
            var ihmJobIds = jobs
                .Where(x => x.ModuleType == SurveyModuleType.IhmSurvey)
                .Select(x => x.Id)
                .Distinct()
                .ToList();

            if (ihmJobIds.Count == 0)
                return new Dictionary<int, IhmSurveyProgress>();

            var totalByJobId = await _db.IhmCheckpoints
                .Where(x => ihmJobIds.Contains(x.SurveyJobId))
                .GroupBy(x => x.SurveyJobId)
                .Select(x => new
                {
                    JobId = x.Key,
                    TotalItems = x.Count()
                })
                .ToDictionaryAsync(x => x.JobId, x => x.TotalItems);

            var completedFromCheckpoints = await _db.IhmCheckpoints
                .Where(x =>
                    ihmJobIds.Contains(x.SurveyJobId) &&
                    x.Result != IhmCheckpointResult.Pending)
                .Select(x => new
                {
                    JobId = x.SurveyJobId,
                    CheckpointId = x.Id
                })
                .ToListAsync();

            var completedFromSubmissions = await _db.IhmCheckpointSubmissions
                .Where(x =>
                    ihmJobIds.Contains(x.SurveyJobId) &&
                    (x.Status == IhmCheckpointSubmissionStatus.Submitted ||
                     x.Status == IhmCheckpointSubmissionStatus.Approved))
                .Select(x => new
                {
                    JobId = x.SurveyJobId,
                    CheckpointId = x.IhmCheckpointId
                })
                .Distinct()
                .ToListAsync();

            var completedByJobId = completedFromCheckpoints
                .Concat(completedFromSubmissions)
                .GroupBy(x => x.JobId)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(item => item.CheckpointId).Distinct().Count());

            return ihmJobIds.ToDictionary(
                jobId => jobId,
                jobId =>
                {
                    var totalItems = totalByJobId.TryGetValue(jobId, out var total)
                        ? total
                        : 0;
                    var completedItems = completedByJobId.TryGetValue(jobId, out var completed)
                        ? Math.Min(completed, totalItems)
                        : 0;
                    return new IhmSurveyProgress(totalItems, completedItems);
                });
        }

        private static IhmSurveyProgress GetProgressForJob(
            SurveyJob job,
            IReadOnlyDictionary<int, IhmSurveyProgress> progressByJobId)
        {
            return job.ModuleType == SurveyModuleType.IhmSurvey &&
                   progressByJobId.TryGetValue(job.Id, out var progress)
                ? progress
                : IhmSurveyProgress.Empty;
        }

        private static LegacyVscpChecklist? FindLatestLegacyVscpChecklist(int jobId)
        {
            var uploadRoot = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "uploads",
                "ihm-excel");

            if (!Directory.Exists(uploadRoot))
                return null;

            var latestFile = Directory.EnumerateFiles(uploadRoot, $"{jobId}_*.xlsx")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();

            return latestFile == null
                ? null
                : new LegacyVscpChecklist(
                    $"/uploads/ihm-excel/{latestFile.Name}",
                    latestFile.Name,
                    latestFile.LastWriteTimeUtc);
        }

        private static SurveyJobDto ToDto(
            SurveyJob job,
            IReadOnlyDictionary<int, string>? userNames = null,
            IhmSurveyProgress? progress = null,
            bool canEdit = false)
        {
            progress ??= IhmSurveyProgress.Empty;
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
            var vscpChecklist = job.UploadedFiles
                .Where(x => string.Equals(x.FileType, VscpChecklistFileType, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.UploadedAt)
                .FirstOrDefault();
            var legacyVscpChecklist = vscpChecklist == null
                ? FindLatestLegacyVscpChecklist(job.Id)
                : null;

            return new SurveyJobDto
            {
                Id = job.Id,
                ModuleType = job.ModuleType.ToString(),
                JobNumber = job.JobNumber,
                ClientName = job.ClientName,
                ProjectId = job.ProjectId,
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
                DisplayStatus = CalculateDisplayStatus(job, progress.Value),
                CompletedItems = progress.Value.CompletedItems,
                TotalItems = progress.Value.TotalItems,
                CompletionPercentage = progress.Value.TotalItems == 0
                    ? 0
                    : Math.Round((decimal)progress.Value.CompletedItems * 100 / progress.Value.TotalItems, 2),
                CreatedByUserId = job.CreatedByUserId,
                CanEdit = canEdit,
                AssignedUserId = job.AssignedUserId,
                AssignedManagerId = job.AssignedManagerId,
                AssignedManagerName = job.AssignedManagerId.HasValue &&
                    userNames != null &&
                    userNames.TryGetValue(job.AssignedManagerId.Value, out var managerName)
                        ? managerName
                        : null,
                CreatedAt = job.CreatedAt,
                UpdatedAt = job.UpdatedAt,
                AuditPort = job.AuditPort,
                AuditAddress = job.AuditAddress,
                VscpChecklistFilePath = vscpChecklist?.FilePath ?? legacyVscpChecklist?.FilePath,
                VscpChecklistFileName = vscpChecklist?.OriginalFileName ?? legacyVscpChecklist?.FileName,
                VscpChecklistUploadedAt = vscpChecklist?.UploadedAt ?? legacyVscpChecklist?.UploadedAt,

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

        private sealed record LegacyVscpChecklist(
            string FilePath,
            string FileName,
            DateTime UploadedAt);

        private static string CalculateDisplayStatus(
            SurveyJob job,
            IhmSurveyProgress progress)
        {
            if (progress.TotalItems > 0 && progress.CompletedItems >= progress.TotalItems)
                return "Completed";

            if (progress.CompletedItems > 0)
                return "Pending";

            return job.SurveyDate.HasValue
                ? "In Progress"
                : "Not Started";
        }

        private readonly record struct IhmSurveyProgress(
            int TotalItems,
            int CompletedItems)
        {
            public static readonly IhmSurveyProgress Empty = new(0, 0);
        }

        private sealed record SurveyorAssignmentTarget(
            int SurveyorUserId,
            string Role);

        private sealed record SurveyorAssignmentEmailRecipient(
            User User,
            string Role,
            bool IsReassignment);

        public class CreateSurveyJobRequest
        {
            public SurveyModuleType ModuleType { get; set; }
            public int? ProjectId { get; set; }
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
            public int? AssignedManagerId { get; set; }
        }

        public class UpdateSurveyJobRequest
        {
            public string? ClientName { get; set; }

            public string? ProjectName { get; set; }
            public int? VesselId { get; set; }

            public string? VesselName { get; set; }

            public string? ImoNumber { get; set; }

            public string? SiteName { get; set; }

            public string? AuditPort { get; set; }
            public string? AuditAddress { get; set; }
            public int? AssignedManagerId { get; set; }


            public DateTime? StartDate { get; set; }
            public DateTime? SurveyDate { get; set; }

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
            public int? ProjectId { get; set; }

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
            public string DisplayStatus { get; set; } = "Not Started";
            public int CompletedItems { get; set; }
            public int TotalItems { get; set; }
            public decimal CompletionPercentage { get; set; }

            public int CreatedByUserId { get; set; }
            public bool CanEdit { get; set; }
            public int? AssignedUserId { get; set; }
            public int? AssignedManagerId { get; set; }
            public string? AssignedManagerName { get; set; }

            public DateTime CreatedAt { get; set; }

            public DateTime? UpdatedAt { get; set; }
            public string? VscpChecklistFilePath { get; set; }
            public string? VscpChecklistFileName { get; set; }
            public DateTime? VscpChecklistUploadedAt { get; set; }

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
            public int LiveIhmJobs { get; set; }
            public int IhmLiveJobs { get; set; }
            public int IhmCompletedJobs { get; set; }
            public int ExSurveyLiveJobs { get; set; }
            public int ExSurveyReports { get; set; }
            public int AttentionJobs { get; set; }
            public int ThisWeekCompletedJobs { get; set; }
            public int ThisMonthCompletedJobs { get; set; }
            public int ThisYearCompletedJobs { get; set; }
            public int OverallCompletedJobs { get; set; }
            public int IhmUsersCount { get; set; }
            public int ExSurveyUsersCount { get; set; }
            public List<SurveyCompletionTrendPointDto> CompletionTrend { get; set; } = new();
        }

        public class SurveyCompletionTrendPointDto
        {
            public string Label { get; set; } = string.Empty;
            public int Count { get; set; }
        }

        public class SurveyorDashboardSummaryDto
        {
            public int LiveJobs { get; set; }
            public int CompletedJobs { get; set; }
            public int DueTodayJobs { get; set; }
        }

        private sealed record IhmAuditJobClassification(
            int JobId,
            int TotalAuditItems,
            int CompletedAuditItems,
            int PendingAuditItems,
            decimal Percentage,
            bool IsCompleted);

    }
}
