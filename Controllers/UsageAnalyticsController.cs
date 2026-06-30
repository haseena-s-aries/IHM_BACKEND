using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Survey_Certification_App.Data;

namespace Survey_Certification_App.Controllers
{
    [ApiController]
    [Authorize(Roles = "Admin")]
    [Route("api/usage-analytics")]
    public class UsageAnalyticsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public UsageAnalyticsController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetUsageAnalytics(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? role,
            [FromQuery] string? department,
            [FromQuery] int? userId,
            [FromQuery] string? module,
            [FromQuery] string? activityStatus)
        {
            var currentRole =
                User.FindFirstValue(ClaimTypes.Role) ??
                User.FindFirstValue("role");

            if (!string.Equals(currentRole, "Admin", StringComparison.OrdinalIgnoreCase))
                return Forbid();

            var utcToday = DateTime.UtcNow.Date;
            var rangeFrom = (from ?? utcToday.AddDays(-30)).Date;
            var rangeTo = (to ?? utcToday).Date.AddDays(1).AddTicks(-1);

            if (rangeTo < rangeFrom)
                return BadRequest(new { message = "The end date must be on or after the start date." });

            var users = await _db.users
                .AsNoTracking()
                .Select(x => new AnalyticsUser(
                    x.Id,
                    x.FullName,
                    x.Email,
                    x.Username,
                    x.Role,
                    x.FieldName,
                    x.IsApproved,
                    x.IsRejected,
                    x.CreatedAt))
                .ToListAsync();

            var allActivities = new List<ActivityFact>();
            allActivities.AddRange(await GetSurveyJobActivities());
            allActivities.AddRange(await GetSurveyAssignmentActivities());
            allActivities.AddRange(await GetSurveyUploadActivities());
            allActivities.AddRange(await GetSurveyPhotoActivities());
            allActivities.AddRange(await GetExSurveyActivities());
            allActivities.AddRange(await GetIhmSurveyActivities());
            allActivities.AddRange(await GetUserRegistrationActivities());

            var filteredUsers = users.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(role))
            {
                filteredUsers = filteredUsers.Where(x =>
                    string.Equals(x.Role, role, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(department))
            {
                filteredUsers = filteredUsers.Where(x =>
                    string.Equals(x.Department, department, StringComparison.OrdinalIgnoreCase));
            }

            if (userId.HasValue)
                filteredUsers = filteredUsers.Where(x => x.Id == userId.Value);

            var filteredUserList = filteredUsers.ToList();
            var allowedUserIds = filteredUserList.Select(x => x.Id).ToHashSet();

            var activityQuery = allActivities
                .Where(x => x.ActivityDate >= rangeFrom && x.ActivityDate <= rangeTo)
                .Where(x => !x.UserId.HasValue || allowedUserIds.Contains(x.UserId.Value));

            if (!string.IsNullOrWhiteSpace(module))
            {
                activityQuery = activityQuery.Where(x =>
                    string.Equals(x.ModuleName, module, StringComparison.OrdinalIgnoreCase));
            }

            var rangeActivities = activityQuery.ToList();
            var lastActivityByUser = allActivities
                .Where(x => x.UserId.HasValue)
                .GroupBy(x => x.UserId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        LastActivityDate = g.Max(x => x.ActivityDate),
                        TotalActivityCount = g.Count()
                    });

            var userActivityRows = filteredUserList
                .Select(x =>
                {
                    lastActivityByUser.TryGetValue(x.Id, out var activity);
                    var status = GetActivityStatus(activity?.LastActivityDate, utcToday);
                    return new
                    {
                        userId = x.Id,
                        userName = DisplayName(x),
                        name = DisplayName(x),
                        login = x.Username ?? x.Email,
                        email = x.Email,
                        role = x.Role,
                        department = x.Department,
                        totalActivityCount = activity?.TotalActivityCount ?? 0,
                        lastActivityDate = activity?.LastActivityDate,
                        activityStatus = status
                    };
                })
                .Where(x => string.IsNullOrWhiteSpace(activityStatus) ||
                            string.Equals(x.activityStatus, activityStatus, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.lastActivityDate)
                .ThenBy(x => x.name)
                .ToList();

            var activeUserIdsInRange = rangeActivities
                .Where(x => x.UserId.HasValue)
                .Select(x => x.UserId!.Value)
                .Distinct()
                .ToHashSet();

            var moduleRows = rangeActivities
                .GroupBy(x => x.ModuleName)
                .Select(g => new
                {
                    moduleName = g.Key,
                    activityCount = g.Count(),
                    createdRecordCount = g.Count(x => x.ActivityKind == ActivityKind.Created),
                    updatedRecordCount = g.Count(x => x.ActivityKind == ActivityKind.Updated),
                    lastActivityDate = g.Max(x => x.ActivityDate),
                    uniqueActiveUsers = g.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value).Distinct().Count()
                })
                .OrderByDescending(x => x.activityCount)
                .ToList();

            var monthlyTrendStart = new DateTime(utcToday.Year, utcToday.Month, 1).AddMonths(-11);
            var monthlyTrend = allActivities
                .Where(x => x.ActivityDate >= monthlyTrendStart)
                .Where(x => string.IsNullOrWhiteSpace(module) ||
                            string.Equals(x.ModuleName, module, StringComparison.OrdinalIgnoreCase))
                .Where(x => !x.UserId.HasValue || allowedUserIds.Contains(x.UserId.Value))
                .GroupBy(x => new { x.ActivityDate.Year, x.ActivityDate.Month, x.ModuleName })
                .Select(g => new
                {
                    month = $"{g.Key.Year:D4}-{g.Key.Month:D2}",
                    moduleName = g.Key.ModuleName,
                    activityCount = g.Count(),
                    uniqueActiveUsers = g.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value).Distinct().Count()
                })
                .OrderBy(x => x.month)
                .ThenBy(x => x.moduleName)
                .ToList();

            var response = new
            {
                filters = new
                {
                    from = rangeFrom,
                    to = rangeTo,
                    role,
                    department,
                    userId,
                    module,
                    activityStatus
                },
                userSummary = new
                {
                    totalRegisteredUsers = filteredUserList.Count,
                    activeUsers = filteredUserList.Count(x => x.IsApproved && !x.IsRejected),
                    inactiveUsers = filteredUserList.Count(x => !x.IsApproved || x.IsRejected),
                    inactiveOrDisabledUsers = filteredUserList.Count(x => !x.IsApproved || x.IsRejected),
                    usersWithActivityInRange = activeUserIdsInRange.Count,
                    usersWithActivityInSelectedRange = activeUserIdsInRange.Count,
                    usersWithNoActivityLast30Days = CountInactiveSince(filteredUserList, lastActivityByUser.ToDictionary(x => x.Key, x => x.Value.LastActivityDate), utcToday.AddDays(-30)),
                    usersWithNoActivityLast60Days = CountInactiveSince(filteredUserList, lastActivityByUser.ToDictionary(x => x.Key, x => x.Value.LastActivityDate), utcToday.AddDays(-60)),
                    usersWithNoActivityLast90Days = CountInactiveSince(filteredUserList, lastActivityByUser.ToDictionary(x => x.Key, x => x.Value.LastActivityDate), utcToday.AddDays(-90))
                },
                departmentSummary = filteredUserList
                    .Where(x => !string.IsNullOrWhiteSpace(x.Department))
                    .GroupBy(x => x.Department!)
                    .Select(g => new
                    {
                        name = g.Key,
                        department = g.Key,
                        userCount = g.Count(),
                        activeUserCount = g.Count(x => x.IsApproved && !x.IsRejected),
                        activeUsers = g.Count(x => x.IsApproved && !x.IsRejected)
                    })
                    .OrderBy(x => x.department)
                    .DefaultIfEmpty(new { name = "Not available", department = "Not available", userCount = 0, activeUserCount = 0, activeUsers = 0 })
                    .ToList(),
                roleSummary = filteredUserList
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.Role) ? "Not available" : x.Role)
                    .Select(g => new
                    {
                        name = g.Key,
                        role = g.Key,
                        userCount = g.Count(),
                        activeUserCount = g.Count(x => x.IsApproved && !x.IsRejected),
                        activeUsers = g.Count(x => x.IsApproved && !x.IsRejected)
                    })
                    .OrderBy(x => x.role)
                    .ToList(),
                moduleWiseUsage = moduleRows,
                lastActivityPerUser = userActivityRows,
                monthlyUsageTrend = monthlyTrend,
                topLists = new
                {
                    topActiveUsers = userActivityRows
                        .OrderByDescending(x => x.totalActivityCount)
                        .ThenByDescending(x => x.lastActivityDate)
                        .Take(10),
                    mostUsedModules = moduleRows
                        .OrderByDescending(x => x.activityCount)
                        .Take(10),
                    leastUsedModules = moduleRows
                        .OrderBy(x => x.activityCount)
                        .Take(10),
                    recentlyActiveUsers = userActivityRows
                        .Where(x => x.lastActivityDate != null)
                        .OrderByDescending(x => x.lastActivityDate)
                        .Take(10),
                    neverActiveUsers = userActivityRows
                        .Where(x => x.lastActivityDate == null)
                        .OrderBy(x => x.name)
                        .Take(25)
                },
                availableFilters = new
                {
                    roles = users.Select(x => x.Role).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x),
                    departments = users.Select(x => x.Department).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x),
                    users = users.OrderBy(DisplayName).Select(x => new { id = x.Id, name = DisplayName(x), email = x.Email }),
                    modules = allActivities.Select(x => x.ModuleName).Distinct().OrderBy(x => x),
                    activityStatuses = new[] { "Active", "Inactive 30+ Days", "Inactive 60+ Days", "Inactive 90+ Days", "Never Active" }
                },
                limitationNote = "Version 1 analytics are calculated from existing database records such as created/updated records, audit logs, and transaction tables. It may not include users who only view pages or dashboards without creating or updating records.",
                implementationReport = new
                {
                    tablesUsed = new[]
                    {
                        "users",
                        "SurveyJobs",
                        "SurveyAssignments",
                        "SurveyUploadedFiles",
                        "SurveyPhotos",
                        "ExEquipments",
                        "ExChecklistAnswers",
                        "ExDefects",
                        "IhmCheckpoints",
                        "IhmCheckpointSubmissions",
                        "IhmImageMarkers",
                        "IhmSamplingPhotos",
                        "UserNotifications"
                    },
                    columnsUsed = new[]
                    {
                        "users: Id, FullName, Email, Username, Role, FieldName, IsApproved, IsRejected, CreatedAt",
                        "SurveyJobs: CreatedByUserId, CreatedAt, UpdatedAt",
                        "SurveyAssignments: SurveyorUserId, AssignedByUserId, AssignedAt",
                        "SurveyUploadedFiles: UploadedByUserId, UploadedAt",
                        "SurveyPhotos: UploadedByUserId, UploadedAt",
                        "ExEquipments: CreatedAt",
                        "ExChecklistAnswers: AnsweredByUserId, AnsweredAt",
                        "ExDefects: CreatedByUserId, CreatedAt, ClosedAt",
                        "IhmCheckpoints: UpdatedByUserId, UpdatedAt",
                        "IhmCheckpointSubmissions: SurveyorUserId, SubmittedAt, ReviewedByUserId, ReviewedAt",
                        "IhmImageMarkers: CreatedByUserId, CreatedAt",
                        "IhmSamplingPhotos: UploadedByUserId, UploadedAt",
                        "UserNotifications: UserId, CreatedAt, ReadAt"
                    },
                    metricsAvailable = new[]
                    {
                        "registered users",
                        "approved/rejected user status",
                        "role summary",
                        "department/team summary from users.FieldName",
                        "module usage from existing created/updated/uploaded/submitted/answered records",
                        "last activity per user inferred from user/action columns",
                        "monthly usage trend for last 12 months",
                        "top active users and module lists"
                    },
                    metricsNotAvailable = new[]
                    {
                        "page views/dashboard views",
                        "login activity history",
                        "session duration",
                        "clickstream",
                        "department/team mapping beyond users.FieldName"
                    },
                    filesChanged = new[]
                    {
                        "Controllers/UsageAnalyticsController.cs"
                    },
                    limitations = new[]
                    {
                        "No login/audit table was found, so last activity is inferred from transaction records.",
                        "Records without user id columns are counted for module usage but cannot contribute to unique active users.",
                        "The endpoint is restricted to Admin role only."
                    }
                }
            };

            return Ok(response);
        }

        private async Task<List<ActivityFact>> GetSurveyJobActivities()
        {
            var rows = await _db.SurveyJobs.AsNoTracking()
                .Select(x => new
                {
                    x.CreatedByUserId,
                    x.CreatedAt,
                    x.UpdatedAt
                })
                .ToListAsync();

            var facts = new List<ActivityFact>();
            foreach (var row in rows)
            {
                facts.Add(new ActivityFact("Survey Jobs", ActivityKind.Created, row.CreatedAt, row.CreatedByUserId));
                if (row.UpdatedAt.HasValue)
                    facts.Add(new ActivityFact("Survey Jobs", ActivityKind.Updated, row.UpdatedAt.Value, row.CreatedByUserId));
            }
            return facts;
        }

        private async Task<List<ActivityFact>> GetSurveyAssignmentActivities()
        {
            var rows = await _db.SurveyAssignments.AsNoTracking()
                .Select(x => new { x.SurveyorUserId, x.AssignedByUserId, x.AssignedAt })
                .ToListAsync();

            var facts = new List<ActivityFact>();
            foreach (var row in rows)
            {
                facts.Add(new ActivityFact("Survey Assignments", ActivityKind.Created, row.AssignedAt, row.AssignedByUserId));
                facts.Add(new ActivityFact("Survey Assignments", ActivityKind.Updated, row.AssignedAt, row.SurveyorUserId));
            }
            return facts;
        }

        private async Task<List<ActivityFact>> GetSurveyUploadActivities()
        {
            return await _db.SurveyUploadedFiles.AsNoTracking()
                .Select(x => new ActivityFact("Survey Uploaded Files", ActivityKind.Created, x.UploadedAt, x.UploadedByUserId))
                .ToListAsync();
        }

        private async Task<List<ActivityFact>> GetSurveyPhotoActivities()
        {
            return await _db.SurveyPhotos.AsNoTracking()
                .Select(x => new ActivityFact("Survey Photos", ActivityKind.Created, x.UploadedAt, x.UploadedByUserId))
                .ToListAsync();
        }

        private async Task<List<ActivityFact>> GetExSurveyActivities()
        {
            var facts = new List<ActivityFact>();

            facts.AddRange(await _db.ExEquipments.AsNoTracking()
                .Select(x => new ActivityFact("EX Equipment", ActivityKind.Created, x.CreatedAt, null))
                .ToListAsync());

            facts.AddRange(await _db.ExChecklistAnswers.AsNoTracking()
                .Select(x => new ActivityFact("EX Checklist Answers", ActivityKind.Created, x.AnsweredAt, x.AnsweredByUserId))
                .ToListAsync());

            var defects = await _db.ExDefects.AsNoTracking()
                .Select(x => new { x.CreatedByUserId, x.CreatedAt, x.ClosedAt })
                .ToListAsync();

            foreach (var row in defects)
            {
                facts.Add(new ActivityFact("EX Defects", ActivityKind.Created, row.CreatedAt, row.CreatedByUserId));
                if (row.ClosedAt.HasValue)
                    facts.Add(new ActivityFact("EX Defects", ActivityKind.Updated, row.ClosedAt.Value, row.CreatedByUserId));
            }

            return facts;
        }

        private async Task<List<ActivityFact>> GetIhmSurveyActivities()
        {
            var facts = new List<ActivityFact>();

            facts.AddRange(await _db.IhmCheckpoints.AsNoTracking()
                .Where(x => x.UpdatedAt.HasValue)
                .Select(x => new ActivityFact("IHM Checkpoints", ActivityKind.Updated, x.UpdatedAt!.Value, x.UpdatedByUserId))
                .ToListAsync());

            var submissions = await _db.IhmCheckpointSubmissions.AsNoTracking()
                .Select(x => new { x.SurveyorUserId, x.SubmittedAt, x.ReviewedByUserId, x.ReviewedAt })
                .ToListAsync();

            foreach (var row in submissions)
            {
                facts.Add(new ActivityFact("IHM Checkpoint Submissions", ActivityKind.Created, row.SubmittedAt, row.SurveyorUserId));
                if (row.ReviewedAt.HasValue)
                    facts.Add(new ActivityFact("IHM Checkpoint Submissions", ActivityKind.Updated, row.ReviewedAt.Value, row.ReviewedByUserId));
            }

            facts.AddRange(await _db.IhmImageMarkers.AsNoTracking()
                .Select(x => new ActivityFact("IHM Layout Markers", ActivityKind.Created, x.CreatedAt, x.CreatedByUserId))
                .ToListAsync());

            facts.AddRange(await _db.IhmSamplingPhotos.AsNoTracking()
                .Select(x => new ActivityFact("IHM Sampling Photos", ActivityKind.Created, x.UploadedAt, x.UploadedByUserId))
                .ToListAsync());

            return facts;
        }

        private async Task<List<ActivityFact>> GetUserRegistrationActivities()
        {
            var facts = await _db.users.AsNoTracking()
                .Select(x => new ActivityFact("User Registration", ActivityKind.Created, x.CreatedAt, x.Id))
                .ToListAsync();

            facts.AddRange(await _db.UserNotifications.AsNoTracking()
                .Select(x => new ActivityFact("User Notifications", ActivityKind.Created, x.CreatedAt, x.UserId))
                .ToListAsync());

            facts.AddRange(await _db.UserNotifications.AsNoTracking()
                .Where(x => x.ReadAt.HasValue)
                .Select(x => new ActivityFact("User Notifications", ActivityKind.Updated, x.ReadAt!.Value, x.UserId))
                .ToListAsync());

            return facts;
        }

        private static int CountInactiveSince(
            IEnumerable<AnalyticsUser> users,
            IReadOnlyDictionary<int, DateTime> lastActivityByUser,
            DateTime cutoff)
        {
            return users.Count(user =>
                !lastActivityByUser.TryGetValue(user.Id, out var lastActivity) ||
                lastActivity < cutoff);
        }

        private static string GetActivityStatus(DateTime? lastActivityDate, DateTime today)
        {
            if (!lastActivityDate.HasValue)
                return "Never Active";

            var inactiveDays = (today - lastActivityDate.Value.Date).TotalDays;

            if (inactiveDays >= 90)
                return "Inactive 90+ Days";

            if (inactiveDays >= 60)
                return "Inactive 60+ Days";

            if (inactiveDays >= 30)
                return "Inactive 30+ Days";

            return "Active";
        }

        private static string DisplayName(AnalyticsUser user)
        {
            if (!string.IsNullOrWhiteSpace(user.FullName))
                return user.FullName;

            if (!string.IsNullOrWhiteSpace(user.Username))
                return user.Username;

            if (!string.IsNullOrWhiteSpace(user.Email))
                return user.Email;

            return $"User #{user.Id}";
        }

        private enum ActivityKind
        {
            Created,
            Updated
        }

        private sealed record AnalyticsUser(
            int Id,
            string FullName,
            string Email,
            string? Username,
            string Role,
            string? Department,
            bool IsApproved,
            bool IsRejected,
            DateTime CreatedAt);

        private sealed record ActivityFact(
            string ModuleName,
            ActivityKind ActivityKind,
            DateTime ActivityDate,
            int? UserId);
    }
}
