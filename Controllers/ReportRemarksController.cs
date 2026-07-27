using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Survey_Certification_App.Data;
using Survey_Certification_App.Models.Survey;
using System.Security.Claims;

namespace Survey_Certification_App.Controllers
{
    [Route("api/report-remarks")]
    [ApiController]
    public class ReportRemarksController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public ReportRemarksController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateReportRemarkRequest request)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Remark))
                return BadRequest(new { message = "Please type a remark." });

            var remark = new ReportClientRemark
            {
                ReportType = request.ReportType.Trim().ToLowerInvariant(),
                ReportId = request.ReportId,
                ClientUserId = userId.Value,
                Remark = request.Remark.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.ReportClientRemarks.Add(remark);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Remark sent successfully." });
        }
        [HttpGet("unread-count")]
        public async Task<IActionResult> UnreadCount()
        {
            var count = await _db.ReportClientRemarks
                .AsNoTracking()
                .CountAsync(x => !x.IsRead);

            return Ok(new { count });
        }

        [HttpPost("mark-read")]
        public async Task<IActionResult> MarkRead()
        {
            var unreadRemarks = await _db.ReportClientRemarks
                .Where(x => !x.IsRead)
                .ToListAsync();

            foreach (var remark in unreadRemarks)
            {
                remark.IsRead = true;
                remark.ReadAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            return Ok(new { message = "Remarks marked as read." });
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var remarks = await _db.ReportClientRemarks
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAtUtc)
                .ToListAsync();

            var reportIds = remarks
                .Where(x => x.ReportType == "ihm")
                .Select(x => x.ReportId)
                .Distinct()
                .ToList();

            var jobsById = await _db.SurveyJobs
                .AsNoTracking()
                .Where(x => reportIds.Contains(x.Id))
                .Select(x => new
                {
                    x.Id,
                    x.VesselName,
                    x.JobNumber
                })
                .ToDictionaryAsync(x => x.Id);

            var reportInfosByJobId = await _db.IhmReportInfos
                .AsNoTracking()
                .Where(x => reportIds.Contains(x.SurveyJobId))
                .Select(x => new
                {
                    x.SurveyJobId,
                    x.RevisionNo
                })
                .ToDictionaryAsync(x => x.SurveyJobId);

            var clientUserIds = remarks
                .Select(x => x.ClientUserId)
                .Distinct()
                .ToList();

            var usersById = await _db.users
                .AsNoTracking()
                .Where(x => clientUserIds.Contains(x.Id))
                .Select(x => new
                {
                    x.Id,
                    x.FullName,
                    x.Email
                })
                .ToDictionaryAsync(x => x.Id);

            var result = remarks.Select(x =>
            {
                jobsById.TryGetValue(x.ReportId, out var job);
                reportInfosByJobId.TryGetValue(x.ReportId, out var info);
                usersById.TryGetValue(x.ClientUserId, out var user);

                return new
                {
                    x.Id,
                    x.ReportType,
                    x.ReportId,
                    x.ClientUserId,
                    ClientName = user?.FullName,
                    ClientEmail = user?.Email,
                    VesselName = job?.VesselName,
                    JobNumber = job?.JobNumber,
                    RevisionNo = info?.RevisionNo,
                    x.Remark,
                    CreatedAtUtc = DateTime.SpecifyKind(x.CreatedAtUtc, DateTimeKind.Utc)
                };
            });

            return Ok(result);
        }

        private int? GetCurrentUserId()
        {
            var value =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub") ??
                User.FindFirstValue("userId");

            return int.TryParse(value, out var userId) ? userId : null;
        }
    }

    public class CreateReportRemarkRequest
    {
        public string ReportType { get; set; } = string.Empty;
        public int ReportId { get; set; }
        public string Remark { get; set; } = string.Empty;
    }
}