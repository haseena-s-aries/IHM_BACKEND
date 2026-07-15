using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Survey_Certification_App.Data;
using Survey_Certification_App.Models;
using Survey_Certification_App.Models.Survey;
using Survey_Certification_App.Services;

namespace Survey_Certification_App.Controllers
{
    [Route("api/report-assignments")]
    [ApiController]
    [Authorize]
    public class ReportAssignmentsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly AccessScopeService _access;

        public ReportAssignmentsController(ApplicationDbContext db, AccessScopeService access)
        {
            _db = db;
            _access = access;
        }

        [HttpGet("clients")]
        public async Task<IActionResult> GetEligibleClients([FromQuery] string projectType, [FromQuery] string? search = null)
        {
            var projectId = await ResolveClientProjectIdAsync(projectType);
            if (!projectId.HasValue)
                return BadRequest(new { message = "Invalid project type." });

            if (!_access.IsAdmin && !await _access.CanManageProjectAsync(projectId.Value))
                return Forbid();

            var projectNameNeedle = (projectType ?? string.Empty).Trim().ToLowerInvariant().StartsWith("ex")
                ? "ex"
                : "ihm";

            var query = _db.users
                .AsNoTracking()
                .Where(x => x.IsApproved && !x.IsRejected)
                .Where(x =>
                    _db.UserProjectAccesses.Any(access =>
                        access.UserId == x.Id &&
                        access.ProjectId == projectId.Value &&
                        access.IsActive &&
                        access.IsApproved &&
                        !access.IsRejected &&
                        access.RoleName.ToLower() == "client") ||
                    (x.ProjectId == projectId.Value &&
                     ((x.Role != null && x.Role.ToLower() == "client") ||
                      (x.RoleName != null && x.RoleName.ToLower() == "client") ||
                      (x.RequestedRole != null && x.RequestedRole.ToLower() == "client"))) ||
                    (x.ProjectName != null &&
                     x.ProjectName.ToLower().Contains(projectNameNeedle) &&
                     ((x.Role != null && x.Role.ToLower() == "client") ||
                      (x.RoleName != null && x.RoleName.ToLower() == "client") ||
                      (x.RequestedRole != null && x.RequestedRole.ToLower() == "client"))))
                .Where(x =>
                    (x.Role == null || x.Role.ToLower() != "admin") &&
                    (x.RoleName == null || x.RoleName.ToLower() != "admin"));

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(x =>
                    (x.FullName != null && x.FullName.ToLower().Contains(term)) ||
                    (x.Email != null && x.Email.ToLower().Contains(term)));
            }

            var clients = await query
                .OrderBy(x => x.FullName)
                .ThenBy(x => x.Email)
                .Select(x => new ClientOptionDto
                {
                    Id = x.Id,
                    FullName = x.FullName,
                    Email = x.Email,
                    ProjectId = x.ProjectId,
                    ProjectName = x.ProjectName
                })
                .ToListAsync();

            return Ok(clients);
        }

        [HttpGet("{reportType}/{reportId:int}")]
        public async Task<IActionResult> GetReportAssignments(string reportType, int reportId)
        {
            var normalizedType = AccessScopeService.NormalizeReportType(reportType);
            if (normalizedType is not ("ihm" or "exsurvey"))
                return BadRequest(new { message = "Invalid report type." });

            if (!await _access.CanManageReportAsync(normalizedType, reportId))
                return Forbid();

            var assignments = await _db.ReportAssignments
                .AsNoTracking()
                .Include(x => x.ClientUser)
                .Where(x => x.IsActive && x.ReportType == normalizedType && x.ReportId == reportId)
                .OrderByDescending(x => x.AssignedAt)
                .Select(x => new ReportAssignmentDto
                {
                    Id = x.Id,
                    ReportType = x.ReportType,
                    ReportId = x.ReportId,
                    SurveyJobId = x.SurveyJobId,
                    ClientUserId = x.ClientUserId,
                    ClientName = x.ClientUser != null ? x.ClientUser.FullName : null,
                    ClientEmail = x.ClientUser != null ? x.ClientUser.Email : null,
                    AssignedByUserId = x.AssignedByUserId,
                    AssignedAt = x.AssignedAt,
                    IsActive = x.IsActive
                })
                .ToListAsync();

            return Ok(assignments);
        }

        [HttpPost]
        public async Task<IActionResult> AssignReport([FromBody] AssignReportRequest request)
        {
            var normalizedType = AccessScopeService.NormalizeReportType(request.ReportType);
            if (normalizedType is not ("ihm" or "exsurvey"))
                return BadRequest(new { message = "Only IHM and EX-Survey report assignment is currently supported." });

            if (!_access.UserId.HasValue)
                return Unauthorized();

            var assignedByUserId = await ResolveAssignedByUserIdAsync();
            if (!assignedByUserId.HasValue)
                return Unauthorized(new { message = "Current user could not be resolved. Please login again." });

            if (!await ReportExistsAsync(normalizedType, request.ReportId))
                return NotFound(new { message = "Report not found." });

            if (!await _access.CanManageReportAsync(normalizedType, request.ReportId))
                return Forbid();

            var client = await _db.users.FirstOrDefaultAsync(x => x.Id == request.ClientUserId);
            if (client == null)
                return BadRequest(new { message = "Client user not found." });

            if (!client.IsApproved || client.IsRejected)
            {
                return BadRequest(new { message = "Selected user is not an approved Client." });
            }

            var reportProjectId = await _access.GetReportProjectIdAsync(normalizedType, request.ReportId);
            if (!reportProjectId.HasValue)
                return BadRequest(new { message = "Report project could not be resolved." });

            var clientHasProjectAccess = await _db.UserProjectAccesses.AsNoTracking().AnyAsync(access =>
                access.UserId == client.Id &&
                access.ProjectId == reportProjectId.Value &&
                access.IsActive &&
                access.IsApproved &&
                !access.IsRejected &&
                access.RoleName.ToLower() == "client");

            var clientHasPrimaryProjectAccess =
                client.ProjectId == reportProjectId.Value &&
                (string.Equals(client.Role, "Client", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(client.RoleName, "Client", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(client.RequestedRole, "Client", StringComparison.OrdinalIgnoreCase));

            if (!clientHasProjectAccess && !clientHasPrimaryProjectAccess)
                return BadRequest(new { message = "Client project does not match the report project." });

            var existing = await _db.ReportAssignments.FirstOrDefaultAsync(x =>
                x.ReportType == normalizedType &&
                x.ReportId == request.ReportId &&
                x.ClientUserId == request.ClientUserId);

            if (existing != null && existing.IsActive)
                return Ok(new { message = "Report is already assigned to this Client.", assignmentId = existing.Id });

            if (existing != null)
            {
                existing.AssignedByUserId = assignedByUserId.Value;
                existing.AssignedAt = DateTime.UtcNow;
                existing.SurveyJobId = request.SurveyJobId ?? (normalizedType is "ihm" or "exsurvey" ? request.ReportId : null);
                existing.IsActive = true;
                existing.RevokedAt = null;
                existing.RevokedByUserId = null;

                _db.UserNotifications.Add(new UserNotification
                {
                    UserId = client.Id,
                    Type = "ReportAssignment",
                    Title = "Report assigned",
                    Message = $"{ToReportLabel(normalizedType)} has been assigned to you.",
                    SurveyJobId = existing.SurveyJobId,
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                return Ok(new { message = "Report assigned successfully.", assignmentId = existing.Id });
            }

            var assignment = new ReportAssignment
            {
                ReportType = normalizedType,
                ReportId = request.ReportId,
                SurveyJobId = request.SurveyJobId ?? (normalizedType is "ihm" or "exsurvey" ? request.ReportId : null),
                ClientUserId = request.ClientUserId,
                AssignedByUserId = assignedByUserId.Value,
                AssignedAt = DateTime.UtcNow,
                IsActive = true
            };

            _db.ReportAssignments.Add(assignment);
            _db.UserNotifications.Add(new UserNotification
            {
                UserId = client.Id,
                Type = "ReportAssignment",
                Title = "Report assigned",
                Message = $"{ToReportLabel(normalizedType)} has been assigned to you.",
                SurveyJobId = assignment.SurveyJobId,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            return Ok(new { message = "Report assigned successfully.", assignmentId = assignment.Id });
        }

        [HttpPost("{assignmentId:int}/revoke")]
        public async Task<IActionResult> RevokeAssignment(int assignmentId)
        {
            if (!_access.UserId.HasValue)
                return Unauthorized();

            var revokedByUserId = await ResolveAssignedByUserIdAsync();
            if (!revokedByUserId.HasValue)
                return Unauthorized(new { message = "Current user could not be resolved. Please login again." });

            var assignment = await _db.ReportAssignments.FirstOrDefaultAsync(x => x.Id == assignmentId);
            if (assignment == null || !assignment.IsActive)
                return NotFound(new { message = "Active assignment not found." });

            if (!await _access.CanManageReportAsync(assignment.ReportType, assignment.ReportId))
                return Forbid();

            assignment.IsActive = false;
            assignment.RevokedAt = DateTime.UtcNow;
            assignment.RevokedByUserId = revokedByUserId.Value;

            _db.UserNotifications.Add(new UserNotification
            {
                UserId = assignment.ClientUserId,
                Type = "ReportAssignmentRevoked",
                Title = "Report access removed",
                Message = $"{ToReportLabel(assignment.ReportType)} access has been removed.",
                SurveyJobId = assignment.SurveyJobId,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            return Ok(new { message = "Report assignment revoked." });
        }

        [HttpDelete("{assignmentId:int}")]
        public Task<IActionResult> DeleteAssignment(int assignmentId) => RevokeAssignment(assignmentId);

        [HttpGet("my-reports")]
        public async Task<IActionResult> GetMyReports()
        {
            if (!_access.IsClient || !_access.UserId.HasValue)
                return Forbid();

            var assignments = await _db.ReportAssignments
                .AsNoTracking()
                .Where(x => x.IsActive && x.ClientUserId == _access.UserId.Value)
                .OrderByDescending(x => x.AssignedAt)
                .ToListAsync();

            var result = new List<AssignedReportDto>();
            foreach (var assignment in assignments)
            {
                var report = await BuildAssignedReportDtoAsync(assignment);
                if (report != null)
                    result.Add(report);
            }

            return Ok(result);
        }

        private async Task<int?> ResolveClientProjectIdAsync(string projectType)
        {
            var type = (projectType ?? string.Empty).Trim().ToLowerInvariant();
            return type switch
            {
                "ihm" => await _access.ResolveProjectIdAsync("IHM"),
                "exsurvey" or "ex-survey" or "ex survey" => await _access.ResolveProjectIdAsync("EX Survey"),
                _ => null
            };
        }

        private async Task<bool> ReportExistsAsync(string reportType, int reportId)
        {
            return reportType switch
            {
                "ihm" => await _db.SurveyJobs.AsNoTracking().AnyAsync(x =>
                    x.Id == reportId && x.ModuleType == SurveyModuleType.IhmSurvey),
                "exsurvey" => await _db.SurveyJobs.AsNoTracking().AnyAsync(x =>
                    x.Id == reportId && x.ModuleType == SurveyModuleType.ExSurvey),
                _ => false
            };
        }

        private async Task<AssignedReportDto?> BuildAssignedReportDtoAsync(ReportAssignment assignment)
        {
            if (assignment.ReportType == "ihm")
            {
                var job = await _db.SurveyJobs.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == assignment.ReportId && x.ModuleType == SurveyModuleType.IhmSurvey);
                if (job == null) return null;

                var createdBy = await GetUserDisplayNameAsync(job.CreatedByUserId);
                return new AssignedReportDto
                {
                    AssignmentId = assignment.Id,
                    ReportId = job.Id,
                    SurveyJobId = job.Id,
                    ReportType = assignment.ReportType,
                    Title = string.IsNullOrWhiteSpace(job.ProjectName) ? "IHM Survey Report" : job.ProjectName,
                    VesselName = job.VesselName,
                    Reference = job.JobNumber,
                    Status = job.Status.ToString(),
                    CreatedBy = createdBy,
                    AssignedAt = assignment.AssignedAt,
                    HasFile = true,
                    ViewUrl = $"/api/ihm-survey/jobs/{job.Id}/report-pdf",
                    DownloadUrl = $"/api/ihm-survey/jobs/{job.Id}/report-pdf"
                };
            }

            if (assignment.ReportType == "exsurvey")
            {
                var job = await _db.SurveyJobs.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == assignment.ReportId && x.ModuleType == SurveyModuleType.ExSurvey);
                if (job == null) return null;

                var createdBy = await GetUserDisplayNameAsync(job.CreatedByUserId);
                return new AssignedReportDto
                {
                    AssignmentId = assignment.Id,
                    ReportId = job.Id,
                    SurveyJobId = job.Id,
                    ReportType = assignment.ReportType,
                    Title = string.IsNullOrWhiteSpace(job.ProjectName) ? "EX-Survey Report" : job.ProjectName,
                    VesselName = job.VesselName ?? job.SiteName,
                    Reference = job.JobNumber,
                    Status = job.Status.ToString(),
                    CreatedBy = createdBy,
                    AssignedAt = assignment.AssignedAt,
                    HasFile = true,
                    ViewUrl = $"/api/ex-survey/jobs/{job.Id}/report",
                    DownloadUrl = $"/api/ex-survey/jobs/{job.Id}/report"
                };
            }

            return null;
        }

        private async Task<string?> GetUserDisplayNameAsync(int userId)
        {
            var name = await _db.users.AsNoTracking()
                .Where(x => x.Id == userId)
                .Select(x => x.FullName ?? x.Email)
                .FirstOrDefaultAsync();

            return !string.IsNullOrWhiteSpace(name)
                ? name
                : userId > 0 ? $"User {userId}" : null;
        }

        private async Task<int?> ResolveAssignedByUserIdAsync()
        {
            if (_access.UserId.HasValue)
            {
                var exists = await _db.users.AsNoTracking()
                    .AnyAsync(x => x.Id == _access.UserId.Value);
                if (exists)
                    return _access.UserId.Value;
            }

            var email = User.FindFirstValue(ClaimTypes.Email)
                        ?? User.FindFirstValue("email");
            if (!string.IsNullOrWhiteSpace(email))
            {
                var normalizedEmail = email.Trim().ToLowerInvariant();
                var userId = await _db.users.AsNoTracking()
                    .Where(x => x.Email.ToLower() == normalizedEmail)
                    .Select(x => (int?)x.Id)
                    .FirstOrDefaultAsync();
                if (userId.HasValue)
                    return userId.Value;

                if (_access.IsAdmin && normalizedEmail == "admin@local.com")
                {
                    var admin = new User
                    {
                        FullName = "Administrator",
                        Email = normalizedEmail,
                        Role = "Admin",
                        RoleName = "Admin",
                        ProjectName = "All Projects",
                        IsApproved = true,
                        IsRejected = false,
                        PasswordHash = string.Empty
                    };
                    _db.users.Add(admin);
                    await _db.SaveChangesAsync();
                    return admin.Id;
                }
            }

            return null;
        }

        private static string ToReportLabel(string reportType) =>
            reportType == "ihm" ? "IHM report" :
            reportType == "exsurvey" ? "EX-Survey report" :
            "Report";
    }

    public sealed class AssignReportRequest
    {
        public string ReportType { get; set; } = string.Empty;
        public int ReportId { get; set; }
        public int? SurveyJobId { get; set; }
        public int ClientUserId { get; set; }
    }

    public sealed class ClientOptionDto
    {
        public int Id { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public int? ProjectId { get; set; }
        public string? ProjectName { get; set; }
    }

    public sealed class ReportAssignmentDto
    {
        public int Id { get; set; }
        public string ReportType { get; set; } = string.Empty;
        public int ReportId { get; set; }
        public int? SurveyJobId { get; set; }
        public int ClientUserId { get; set; }
        public string? ClientName { get; set; }
        public string? ClientEmail { get; set; }
        public int AssignedByUserId { get; set; }
        public DateTime AssignedAt { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class AssignedReportDto
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
