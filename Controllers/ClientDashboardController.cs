using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Survey_Certification_App.Data;
using Survey_Certification_App.Models;
using Survey_Certification_App.Models.Survey;
using Survey_Certification_App.Services;

namespace Survey_Certification_App.Controllers
{
    [Route("api/client-dashboard")]
    [ApiController]
    [Authorize]
    public class ClientDashboardController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly AccessScopeService _access;

        public ClientDashboardController(ApplicationDbContext db, AccessScopeService access)
        {
            _db = db;
            _access = access;
        }

        [HttpGet("summary")]
        public async Task<IActionResult> Summary()
        {
            if (!_access.IsClient || !_access.UserId.HasValue)
                return Forbid();

            var clientUserId = _access.UserId.Value;
            var assignments = await _db.ReportAssignments
                .AsNoTracking()
                .Where(x => x.IsActive && x.ClientUserId == clientUserId)
                .OrderByDescending(x => x.AssignedAt)
                .ToListAsync();

            var jobIds = assignments
                .Select(x => x.SurveyJobId ?? x.ReportId)
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            var jobs = await _db.SurveyJobs
                .AsNoTracking()
                .Where(x => jobIds.Contains(x.Id))
                .ToListAsync();

            var ihmJobIds = jobs
                .Where(x => x.ModuleType == SurveyModuleType.IhmSurvey)
                .Select(x => x.Id)
                .ToList();

            var areas = await _db.IhmAreas
                .AsNoTracking()
                .Where(x => ihmJobIds.Contains(x.SurveyJobId))
                .ToListAsync();

            var checkpoints = await _db.IhmCheckpoints
                .AsNoTracking()
                .Where(x => ihmJobIds.Contains(x.SurveyJobId))
                .ToListAsync();

            var surveyorIds = jobs
                .Select(x => x.AssignedUserId)
                .Concat(await _db.SurveyAssignments
                    .AsNoTracking()
                    .Where(x => jobIds.Contains(x.SurveyJobId) && x.Status != SurveyAssignmentStatus.Returned)
                    .Select(x => (int?)x.SurveyorUserId)
                    .ToListAsync())
                .Where(x => x.HasValue && x.Value > 0)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            var userIds = surveyorIds
                .Concat(jobs.Select(x => x.CreatedByUserId))
                .Concat(assignments.Select(x => x.AssignedByUserId))
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            var userNames = await _db.users
                .AsNoTracking()
                .Where(x => userIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.FullName ?? x.Email ?? $"User {x.Id}");

            var activeAssignments = await _db.SurveyAssignments
                .AsNoTracking()
                .Where(x => jobIds.Contains(x.SurveyJobId) && x.Status != SurveyAssignmentStatus.Returned)
                .OrderBy(x => x.AssignedAt)
                .ToListAsync();

            var areasByJobId = areas
                .GroupBy(x => x.SurveyJobId)
                .ToDictionary(x => x.Key, x => x.ToList());
            var checkpointsByJobId = checkpoints
                .GroupBy(x => x.SurveyJobId)
                .ToDictionary(x => x.Key, x => x.ToList());
            var assignmentsByJobId = activeAssignments
                .GroupBy(x => x.SurveyJobId)
                .ToDictionary(x => x.Key, x => x.ToList());
            var jobsById = jobs.ToDictionary(x => x.Id);

            var jobCards = jobs
                .OrderByDescending(x => x.CreatedAt)
                .Select(job => BuildJobDto(
                    job,
                    areasByJobId.TryGetValue(job.Id, out var jobAreas) ? jobAreas : new List<IhmArea>(),
                    checkpointsByJobId.TryGetValue(job.Id, out var jobCheckpoints) ? jobCheckpoints : new List<IhmCheckpoint>(),
                    assignmentsByJobId.TryGetValue(job.Id, out var jobAssignments) ? jobAssignments : new List<SurveyAssignment>(),
                    userNames))
                .ToList();

            var reports = assignments
                .Select(x => BuildReportDto(
                    x,
                    jobsById.TryGetValue(x.SurveyJobId ?? x.ReportId, out var job) ? job : null,
                    userNames))
                .Where(x => x != null)
                .Cast<ClientAssignedReportDto>()
                .ToList();

            var kpis = new ClientDashboardKpiDto
            {
                AllAssignedJobs = jobCards.Count,
                CompletedJobs = jobCards.Count(x => x.CurrentStatus == "Completed"),
                InProgressJobs = jobCards.Count(x => x.CurrentStatus == "In Progress"),
                PendingJobs = jobCards.Count(x => x.CurrentStatus == "Pending"),
                AssignedReports = reports.Count,
                AreasCovered = jobCards.Sum(x => x.CompletedAreas)
            };

            return Ok(new ClientDashboardSummaryDto
            {
                Kpis = kpis,
                AssignedJobs = jobCards,
                HazardousMaterials = BuildHazardSummary(checkpoints),
                AssignedReports = reports
            });
        }

        [HttpGet("jobs/{jobId:int}/summary")]
        public async Task<IActionResult> JobSummary(int jobId)
        {
            if (!_access.IsClient || !_access.UserId.HasValue)
                return Forbid();

            var clientUserId = _access.UserId.Value;
            var hasAssignment = await _db.ReportAssignments
                .AsNoTracking()
                .AnyAsync(x =>
                    x.IsActive &&
                    x.ClientUserId == clientUserId &&
                    ((x.SurveyJobId.HasValue && x.SurveyJobId.Value == jobId) ||
                     x.ReportId == jobId));

            if (!hasAssignment)
                return Forbid();

            var job = await _db.SurveyJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == jobId);

            if (job == null)
                return NotFound(new { message = "Survey job was not found." });

            var isIhm = job.ModuleType == SurveyModuleType.IhmSurvey;
            var areas = isIhm
                ? await _db.IhmAreas.AsNoTracking()
                    .Where(x => x.SurveyJobId == jobId)
                    .ToListAsync()
                : new List<IhmArea>();
            var checkpoints = isIhm
                ? await _db.IhmCheckpoints.AsNoTracking()
                    .Where(x => x.SurveyJobId == jobId)
                    .ToListAsync()
                : new List<IhmCheckpoint>();
            var photos = isIhm
                ? await _db.IhmSamplingPhotos.AsNoTracking()
                    .Where(x => x.SurveyJobId == jobId)
                    .ToListAsync()
                : new List<IhmSamplingPhoto>();
            var assignments = await _db.SurveyAssignments
                .AsNoTracking()
                .Where(x => x.SurveyJobId == jobId && x.Status != SurveyAssignmentStatus.Returned)
                .OrderBy(x => x.AssignedAt)
                .ToListAsync();

            var userIds = assignments
                .Select(x => x.SurveyorUserId)
                .Concat(new[] { job.CreatedByUserId })
                .Concat(checkpoints.Where(x => x.UpdatedByUserId.HasValue).Select(x => x.UpdatedByUserId!.Value))
                .Where(x => x > 0)
                .Distinct()
                .ToList();
            if (job.AssignedUserId.HasValue && job.AssignedUserId.Value > 0)
                userIds.Add(job.AssignedUserId.Value);

            var userNames = await _db.users
                .AsNoTracking()
                .Where(x => userIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.FullName ?? x.Email ?? $"User {x.Id}");

            return Ok(new ClientJobSummaryDto
            {
                Job = BuildJobDto(job, areas, checkpoints, assignments, userNames),
                HazardousMaterials = BuildHazardSummary(checkpoints),
                SurveyDetails = BuildActivities(checkpoints, areas, photos, userNames, 100)
            });
        }

        private static ClientJobProgressDto BuildJobDto(
            SurveyJob job,
            List<IhmArea> areas,
            List<IhmCheckpoint> checkpoints,
            List<SurveyAssignment> assignments,
            Dictionary<int, string> userNames)
        {
            var totalCheckpoints = checkpoints.Count;
            var completedCheckpoints = checkpoints.Count(IsCheckpointCompleted);
            var completedAreaIds = checkpoints
                .GroupBy(x => x.IhmAreaId)
                .Where(group => group.Any() && group.All(IsCheckpointCompleted))
                .Select(group => group.Key)
                .ToHashSet();
            var totalAreas = areas.Count;
            var completedAreas = areas.Count(x => completedAreaIds.Contains(x.Id));
            var status = CalculateStatus(job.SurveyDate, completedCheckpoints, totalCheckpoints);
            var surveyorId = assignments.FirstOrDefault()?.SurveyorUserId ?? job.AssignedUserId;

            return new ClientJobProgressDto
            {
                JobId = job.Id,
                JobNumber = job.JobNumber,
                VesselName = job.VesselName,
                Imo = job.ImoNumber,
                SurveyDate = job.SurveyDate,
                SurveyorName = surveyorId.HasValue && userNames.TryGetValue(surveyorId.Value, out var name)
                    ? name
                    : null,
                CurrentStatus = status,
                CompletionPercentage = totalCheckpoints == 0
                    ? 0
                    : Math.Round(completedCheckpoints * 100m / totalCheckpoints, 2),
                TotalAreas = totalAreas,
                CompletedAreas = completedAreas,
                PendingAreas = Math.Max(totalAreas - completedAreas, 0),
                TotalCheckpoints = totalCheckpoints,
                CompletedCheckpoints = completedCheckpoints
            };
        }

        private static ClientHazardousMaterialSummaryDto BuildHazardSummary(List<IhmCheckpoint> checkpoints)
        {
            return new ClientHazardousMaterialSummaryDto
            {
                TotalHazardousMaterialRecords = checkpoints.Count(x => !string.IsNullOrWhiteSpace(x.HazardousMaterial)),
                VisualSamplesCount = checkpoints.Count(x => string.Equals(x.VisualCheck?.Trim(), "V", StringComparison.OrdinalIgnoreCase)),
                PhysicalSamplesCount = checkpoints.Count(x => string.Equals(x.SamplingCheck?.Trim(), "S", StringComparison.OrdinalIgnoreCase)),
                PositiveFindings = checkpoints.Count(x =>
                    x.Result == IhmCheckpointResult.Present ||
                    string.Equals(x.CheckResult?.Trim(), "Yes", StringComparison.OrdinalIgnoreCase)),
                NegativeFindings = checkpoints.Count(x =>
                    x.Result == IhmCheckpointResult.NotPresent ||
                    string.Equals(x.CheckResult?.Trim(), "No", StringComparison.OrdinalIgnoreCase)),
                PendingLabResults = checkpoints.Count(x => x.Result == IhmCheckpointResult.SampleRequired),
                CompletedLabResults = checkpoints.Count(x =>
                    x.Result == IhmCheckpointResult.Present ||
                    x.Result == IhmCheckpointResult.NotPresent ||
                    x.Result == IhmCheckpointResult.NotApplicable)
            };
        }

        private static List<ClientSurveyActivityDto> BuildActivities(
            List<IhmCheckpoint> checkpoints,
            List<IhmArea> areas,
            List<IhmSamplingPhoto> photos,
            Dictionary<int, string> userNames,
            int limit = 25)
        {
            var areaNames = areas.ToDictionary(x => x.Id, x => x.AreaName);
            var photosByCheckpoint = photos
                .Where(x => x.IhmCheckpointId.HasValue)
                .GroupBy(x => x.IhmCheckpointId!.Value)
                .ToDictionary(x => x.Key, x => x.Count());

            return checkpoints
                .Where(IsCheckpointCompleted)
                .OrderByDescending(x => x.UpdatedAt)
                .Take(limit)
                .Select(x => new ClientSurveyActivityDto
                {
                    AreaName = areaNames.TryGetValue(x.IhmAreaId, out var areaName) ? areaName : "Area",
                    CheckpointsCompleted = 1,
                    Remarks = x.Remarks,
                    Location = x.Location,
                    SampleNumber = x.Sample_No,
                    VisualSampleType = string.Equals(x.SamplingCheck?.Trim(), "S", StringComparison.OrdinalIgnoreCase)
                        ? "Physical Sample"
                        : string.Equals(x.VisualCheck?.Trim(), "V", StringComparison.OrdinalIgnoreCase)
                            ? "Visual"
                            : x.Result.ToString(),
                    SurveyPhotosCount = photosByCheckpoint.TryGetValue(x.Id, out var count) ? count : 0,
                    LastUpdatedDate = x.UpdatedAt,
                    SurveyorName = x.UpdatedByUserId.HasValue && userNames.TryGetValue(x.UpdatedByUserId.Value, out var name)
                        ? name
                        : null
                })
                .ToList();
        }

        private static ClientAssignedReportDto? BuildReportDto(
            ReportAssignment assignment,
            SurveyJob? job,
            Dictionary<int, string> userNames)
        {
            if (job == null) return null;

            return new ClientAssignedReportDto
            {
                AssignmentId = assignment.Id,
                ReportId = assignment.ReportId,
                SurveyJobId = job.Id,
                ReportType = assignment.ReportType,
                Title = string.IsNullOrWhiteSpace(job.ProjectName) ? ToReportLabel(assignment.ReportType) : job.ProjectName,
                VesselName = job.VesselName ?? job.SiteName,
                Reference = job.JobNumber,
                Status = job.Status.ToString(),
                CreatedBy = userNames.TryGetValue(job.CreatedByUserId, out var createdBy) ? createdBy : null,
                AssignedAt = assignment.AssignedAt,
                HasFile = true,
                ViewUrl = assignment.ReportType == "exsurvey"
                    ? $"/api/ex-survey/jobs/{job.Id}/report"
                    : $"/api/ihm-survey/jobs/{job.Id}/report-pdf",
                DownloadUrl = assignment.ReportType == "exsurvey"
                    ? $"/api/ex-survey/jobs/{job.Id}/report"
                    : $"/api/ihm-survey/jobs/{job.Id}/report-pdf"
            };
        }

        private static bool IsCheckpointCompleted(IhmCheckpoint checkpoint) =>
            checkpoint.Result != IhmCheckpointResult.Pending;

        private static string CalculateStatus(DateTime? surveyDate, int completedItems, int totalItems)
        {
            if (totalItems > 0 && completedItems >= totalItems) return "Completed";
            if (completedItems > 0) return "Pending";
            if (surveyDate.HasValue) return "In Progress";
            return "Not Started";
        }

        private static string ToReportLabel(string reportType) =>
            reportType == "ihm" ? "IHM Report" :
            reportType == "exsurvey" ? "EX-Survey Report" :
            "Report";
    }

    public sealed class ClientDashboardSummaryDto
    {
        public ClientDashboardKpiDto Kpis { get; set; } = new();
        public List<ClientJobProgressDto> AssignedJobs { get; set; } = new();
        public ClientHazardousMaterialSummaryDto HazardousMaterials { get; set; } = new();
        public List<ClientAssignedReportDto> AssignedReports { get; set; } = new();
    }

    public sealed class ClientJobSummaryDto
    {
        public ClientJobProgressDto Job { get; set; } = new();
        public ClientHazardousMaterialSummaryDto HazardousMaterials { get; set; } = new();
        public List<ClientSurveyActivityDto> SurveyDetails { get; set; } = new();
    }

    public sealed class ClientDashboardKpiDto
    {
        public int AllAssignedJobs { get; set; }
        public int CompletedJobs { get; set; }
        public int InProgressJobs { get; set; }
        public int PendingJobs { get; set; }
        public int AssignedReports { get; set; }
        public int AreasCovered { get; set; }
    }

    public sealed class ClientJobProgressDto
    {
        public int JobId { get; set; }
        public string JobNumber { get; set; } = string.Empty;
        public string? VesselName { get; set; }
        public string? Imo { get; set; }
        public DateTime? SurveyDate { get; set; }
        public string? SurveyorName { get; set; }
        public string CurrentStatus { get; set; } = "Not Started";
        public decimal CompletionPercentage { get; set; }
        public int TotalAreas { get; set; }
        public int CompletedAreas { get; set; }
        public int PendingAreas { get; set; }
        public int TotalCheckpoints { get; set; }
        public int CompletedCheckpoints { get; set; }
    }

    public sealed class ClientHazardousMaterialSummaryDto
    {
        public int TotalHazardousMaterialRecords { get; set; }
        public int VisualSamplesCount { get; set; }
        public int PhysicalSamplesCount { get; set; }
        public int PositiveFindings { get; set; }
        public int NegativeFindings { get; set; }
        public int PendingLabResults { get; set; }
        public int CompletedLabResults { get; set; }
    }

    public sealed class ClientSurveyActivityDto
    {
        public string AreaName { get; set; } = string.Empty;
        public int CheckpointsCompleted { get; set; }
        public string? Remarks { get; set; }
        public string? Location { get; set; }
        public string? SampleNumber { get; set; }
        public string? VisualSampleType { get; set; }
        public int SurveyPhotosCount { get; set; }
        public DateTime? LastUpdatedDate { get; set; }
        public string? SurveyorName { get; set; }
    }

    public sealed class ClientAssignedReportDto
    {
        public int AssignmentId { get; set; }
        public int ReportId { get; set; }
        public int? SurveyJobId { get; set; }
        public string ReportType { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? VesselName { get; set; }
        public string? Reference { get; set; }
        public string? Status { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime AssignedAt { get; set; }
        public bool HasFile { get; set; }
        public string? ViewUrl { get; set; }
        public string? DownloadUrl { get; set; }
    }
}
