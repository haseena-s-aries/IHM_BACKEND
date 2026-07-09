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
    }
}