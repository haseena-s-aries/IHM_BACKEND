using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Survey_Certification_App.Data;
using Survey_Certification_App.Models.Survey;

namespace Survey_Certification_App.Services
{
    public class AccessScopeService
    {
        private readonly ApplicationDbContext _db;
        private readonly IHttpContextAccessor _http;

        public AccessScopeService(ApplicationDbContext db, IHttpContextAccessor http)
        {
            _db = db;
            _http = http;
        }

        public int? UserId => int.TryParse(
            _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? _http.HttpContext?.User.FindFirstValue("sub"),
            out var id) ? id : null;

        public string Role => _http.HttpContext?.User.FindFirstValue(ClaimTypes.Role)
            ?? _http.HttpContext?.User.FindFirstValue("role")
            ?? "";

        public int? ProjectId => int.TryParse(
            _http.HttpContext?.User.FindFirstValue("projectId"),
            out var id) ? id : null;

        public bool IsAdmin => string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);
        public bool IsManager => string.Equals(Role, "Manager", StringComparison.OrdinalIgnoreCase);
        public bool IsClient => string.Equals(Role, "Client", StringComparison.OrdinalIgnoreCase);
        public bool IsSurveyor => string.Equals(Role, "Surveyor", StringComparison.OrdinalIgnoreCase);

        public static string? NormalizeReportType(string? type)
        {
            var value = (type ?? string.Empty).Trim().ToLowerInvariant();
            return value is "ihm" or "exsurvey" or "towage" or "seafastening" or "loadout"
                ? value
                : null;
        }
        public IQueryable<SurveyJob> FilterJobs(IQueryable<SurveyJob> query)
        {
            if (IsAdmin)
                return query;

            if (IsClient && UserId.HasValue)
            {
                return query.Where(job =>
                    job.VesselId.HasValue &&
                    _db.ClientVesselMappings.Any(map =>
                        map.UserId == UserId.Value &&
                        map.VesselId == job.VesselId.Value));
            }

            if (IsSurveyor && ProjectId.HasValue && UserId.HasValue)
            {
                return query.Where(job =>
                    job.ProjectId == ProjectId.Value &&
                    (
                        job.CreatedByUserId == UserId.Value ||
                        job.AssignedUserId == UserId.Value ||
                        job.Assignments.Any(a =>
                            a.SurveyorUserId == UserId.Value &&
                            a.Status != SurveyAssignmentStatus.Returned)
                    ));
            }

            if (IsSurveyor && ProjectId.HasValue && UserId.HasValue)
            {
                return query.Where(job =>
                    job.ProjectId == ProjectId.Value &&
                    (
                        job.CreatedByUserId == UserId.Value ||
                        job.AssignedUserId == UserId.Value ||
                        job.Assignments.Any(a =>
                            a.SurveyorUserId == UserId.Value &&
                            a.Status != SurveyAssignmentStatus.Returned)
                    ));
            }

            if (ProjectId.HasValue && UserId.HasValue)
            {
                return query.Where(job =>
                    job.ProjectId == ProjectId.Value &&
                    job.CreatedByUserId == UserId.Value);
            }

            return query.Where(x => false);
        }

        public IQueryable<Survey_Certification_App.Models.User> FilterUsers(
            IQueryable<Survey_Certification_App.Models.User> query)
        {
            if (IsAdmin) return query;

            if (IsClient) return query.Where(x => false);

            if (ProjectId.HasValue)
                return query.Where(x => x.ProjectId == ProjectId.Value);

            return query.Where(x => false);
        }

        public async Task<bool> CanManageReportAsync(string reportType, int reportId)
        {
            reportType = NormalizeReportType(reportType) ?? string.Empty;
            if (IsAdmin)
                return true;

            if (!IsManager || !UserId.HasValue)
                return false;

            return reportType switch
            {
                "ihm" => await _db.SurveyJobs.AsNoTracking().AnyAsync(x =>
                    x.Id == reportId &&
                    x.ModuleType == SurveyModuleType.IhmSurvey &&
                    x.ProjectId == ProjectId &&
                    (x.CreatedByUserId == UserId.Value ||
                     x.AssignedManagerId == UserId.Value)),
                "exsurvey" => await _db.SurveyJobs.AsNoTracking().AnyAsync(x =>
                    x.Id == reportId &&
                    x.ModuleType == SurveyModuleType.ExSurvey &&
                    x.ProjectId == ProjectId &&
                    (x.CreatedByUserId == UserId.Value ||
                     x.AssignedManagerId == UserId.Value)),
                "seafastening" => ProjectId == 3 && await _db.SeaFastenings.AsNoTracking()
                    .AnyAsync(x => x.Id == reportId && x.UserId == UserId.Value.ToString()),
                "towage" or "loadout" => false,
                _ => false
            };
        }

        public async Task<bool> CanViewAssignedReportAsync(string reportType, int reportId)
        {
            reportType = NormalizeReportType(reportType) ?? string.Empty;
            return IsClient &&
                   UserId.HasValue &&
                   await _db.ReportAssignments.AsNoTracking().AnyAsync(x =>
                       x.IsActive &&
                       x.ReportType == reportType &&
                       x.ReportId == reportId &&
                       x.ClientUserId == UserId.Value);
        }

        public async Task<bool> CanAccessReportAsync(string reportType, int reportId)
        {
            reportType = NormalizeReportType(reportType) ?? string.Empty;
            if (IsAdmin)
                return true;

            if (await CanViewAssignedReportAsync(reportType, reportId))
                return true;

            if (!UserId.HasValue)
                return false;

            return reportType switch
            {
                "ihm" => await _db.SurveyJobs.AsNoTracking().AnyAsync(x =>
                    x.Id == reportId &&
                    x.ModuleType == SurveyModuleType.IhmSurvey &&
                    (x.CreatedByUserId == UserId.Value ||
                     x.AssignedManagerId == UserId.Value ||
                     x.AssignedUserId == UserId.Value ||
                     x.Assignments.Any(a =>
                         a.SurveyorUserId == UserId.Value &&
                         a.Status != SurveyAssignmentStatus.Returned))),
                "exsurvey" => await _db.SurveyJobs.AsNoTracking().AnyAsync(x =>
                    x.Id == reportId &&
                    x.ModuleType == SurveyModuleType.ExSurvey &&
                    (x.CreatedByUserId == UserId.Value ||
                     x.AssignedManagerId == UserId.Value ||
                     x.AssignedUserId == UserId.Value ||
                     x.Assignments.Any(a =>
                         a.SurveyorUserId == UserId.Value &&
                         a.Status != SurveyAssignmentStatus.Returned))),
                "seafastening" => await _db.SeaFastenings.AsNoTracking().AnyAsync(x =>
                    x.Id == reportId &&
                    x.UserId == UserId.Value.ToString()),
                "towage" or "loadout" => false,
                _ => false
            };
        }

        public async Task<int?> GetReportProjectIdAsync(string reportType, int reportId)
        {
            reportType = NormalizeReportType(reportType) ?? string.Empty;
            if (reportType == "ihm")
            {
                var jobProjectId = await _db.SurveyJobs.AsNoTracking()
                    .Where(x => x.Id == reportId && x.ModuleType == SurveyModuleType.IhmSurvey)
                    .Select(x => x.ProjectId)
                    .FirstOrDefaultAsync();
                return jobProjectId ?? await ResolveProjectIdAsync("IHM");
            }

            if (reportType == "exsurvey")
            {
                var jobProjectId = await _db.SurveyJobs.AsNoTracking()
                    .Where(x => x.Id == reportId && x.ModuleType == SurveyModuleType.ExSurvey)
                    .Select(x => x.ProjectId)
                    .FirstOrDefaultAsync();
                return jobProjectId ?? await ResolveProjectIdAsync("EX Survey");
            }

            if (reportType is "towage" or "seafastening" or "loadout")
                return await ResolveProjectIdAsync("AURAS");

            return null;
        }

        public async Task<int?> ResolveProjectIdAsync(string projectName)
        {
            var normalized = projectName.Trim().ToLower();
            return await _db.Projects.AsNoTracking()
                .Where(x => x.IsActive && x.Name.ToLower() == normalized)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync();
        }
    }
}