using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Survey_Certification_App.Data;
using Survey_Certification_App.Models;
using Survey_Certification_App.Models.Survey;
using Survey_Certification_App.Services;

namespace Survey_Certification_App.Controllers
{
    [Route("api/vessels")]
    [ApiController]
    public class VesselController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly AccessScopeService _access;

        public VesselController(ApplicationDbContext db, AccessScopeService access)
        {
            _db = db;
            _access = access;
        }
        private IQueryable<Vessel> ApplyVesselReadAccessFilter(IQueryable<Vessel> query)
        {
            if (_access.IsAdmin || _access.IsManager)
                return query;

            if (_access.IsClient && _access.UserId.HasValue)
            {
                return query.Where(v =>
                    _db.ClientVesselMappings.Any(m =>
                        m.UserId == _access.UserId.Value &&
                        m.VesselId == v.Id));
            }

            if (_access.IsSurveyor && _access.UserId.HasValue)
            {
                return query.Where(v =>
                    _db.SurveyJobs.Any(job =>
                        job.VesselId == v.Id &&
                        (
                            job.AssignedUserId == _access.UserId.Value ||
                            job.Assignments.Any(a =>
                                a.SurveyorUserId == _access.UserId.Value &&
                                a.Status != SurveyAssignmentStatus.Returned)
                        )));
            }

            return query.Where(v => false);
        }

        private IQueryable<Vessel> ApplyVesselWriteAccessFilter(IQueryable<Vessel> query)
        {
            if (_access.IsAdmin)
                return query;

            if (_access.IsManager && _access.UserId.HasValue)
            {
                return query.Where(v => v.CreatedByUserId == _access.UserId.Value);
            }

            return query.Where(v => false);
        }
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var vessels = await ApplyVesselReadAccessFilter(_db.Vessels)
                .Where(x => x.Status == VesselStatus.Active)
                .OrderBy(x => x.VesselName)
                .ToListAsync();

            return Ok(vessels.Select(ToDto));

        }
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var vessel = await ApplyVesselReadAccessFilter(_db.Vessels)
     .FirstOrDefaultAsync(x => x.Id == id);

            if (vessel == null)
                return NotFound("Vessel not found.");

            return Ok(ToDto(vessel));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveVesselRequest request)
        {
            if (!_access.UserId.HasValue)
                return Unauthorized("Login is required to create a vessel.");

            if (!_access.IsAdmin && !_access.IsManager)
                return Forbid();

            if (string.IsNullOrWhiteSpace(request.VesselName))
                return BadRequest("Vessel name is required.");

            var vessel = new Vessel
            {
                VesselName = request.VesselName,
                ImoNumber = request.ImoNumber,
                OfficialNumber = request.OfficialNumber,
                VesselType = request.VesselType,
                Flag = request.Flag,
                PortOfRegistry = request.PortOfRegistry,
                ClassSociety = request.ClassSociety,
                GrossTonnage = request.GrossTonnage,
                NetTonnage = request.NetTonnage,
                Deadweight = request.Deadweight,
                LengthOverall = request.LengthOverall,
                Breadth = request.Breadth,
                Depth = request.Depth,
                YearBuilt = request.YearBuilt,
                OwnerName = request.OwnerName,
                ManagerName = request.ManagerName,
                ClientName = request.ClientName,
                Status = VesselStatus.Active,
                CreatedAt = DateTime.UtcNow,
                CallSign = request.CallSign,
                Builder = request.Builder,
                DateOfBuild = request.DateOfBuild,
                LengthBetweenPerpendiculars = request.LengthBetweenPerpendiculars,
                DraughtMoulded = request.DraughtMoulded,
                CreatedByUserId = _access.UserId.Value,
            };
            if (!_access.IsAdmin)
            {
                if (!_access.ProjectId.HasValue || !_access.UserId.HasValue)
                    return Forbid();

                vessel.ProjectId = _access.ProjectId.Value;
                vessel.ProjectName = User.FindFirst("projectName")?.Value;
            }
            else
            {
                vessel.ProjectId = request.ProjectId;

                if (request.ProjectId.HasValue && request.ProjectId.Value > 0)
                {
                    var project = await _db.Projects
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == request.ProjectId.Value);

                    vessel.ProjectName = project?.Name;
                }
                else
                {
                    vessel.ProjectId = null;
                    vessel.ProjectName = null;
                }
            }

            _db.Vessels.Add(vessel);
            await _db.SaveChangesAsync();

            return Ok(ToDto(vessel));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SaveVesselRequest request)
        {
            if (!_access.IsAdmin && !_access.IsManager)
                return Forbid();

            var vessel = await ApplyVesselWriteAccessFilter(_db.Vessels)
    .FirstOrDefaultAsync(x => x.Id == id);

            if (vessel == null)
                return NotFound("Vessel not found.");

            vessel.VesselName = request.VesselName;
            vessel.ImoNumber = request.ImoNumber;
            vessel.OfficialNumber = request.OfficialNumber;
            vessel.VesselType = request.VesselType;
            vessel.Flag = request.Flag;
            vessel.PortOfRegistry = request.PortOfRegistry;
            vessel.ClassSociety = request.ClassSociety;
            vessel.GrossTonnage = request.GrossTonnage;
            vessel.NetTonnage = request.NetTonnage;
            vessel.Deadweight = request.Deadweight;
            vessel.LengthOverall = request.LengthOverall;
            vessel.Breadth = request.Breadth;
            vessel.Depth = request.Depth;
            vessel.YearBuilt = request.YearBuilt;
            vessel.OwnerName = request.OwnerName;
            vessel.ManagerName = request.ManagerName;
            vessel.ClientName = request.ClientName;
            vessel.UpdatedAt = DateTime.UtcNow;
            vessel.CallSign = request.CallSign;
            vessel.Builder = request.Builder;
            vessel.DateOfBuild = request.DateOfBuild;
            vessel.LengthBetweenPerpendiculars = request.LengthBetweenPerpendiculars;
            vessel.DraughtMoulded = request.DraughtMoulded;

            await _db.SaveChangesAsync();

            return Ok(ToDto(vessel));
        }

        [HttpPost("{id:int}/deactivate")]
        public async Task<IActionResult> Deactivate(int id)
        {
            if (!_access.IsAdmin && !_access.IsManager)
                return Forbid();

            var vessel = await ApplyVesselWriteAccessFilter(_db.Vessels)
    .FirstOrDefaultAsync(x => x.Id == id);

            if (vessel == null)
                return NotFound("Vessel not found.");

            var linkedJobCount = await _db.SurveyJobs
                .AsNoTracking()
                .CountAsync(x => x.VesselId == id);

            if (linkedJobCount > 0)
            {
                return Conflict(new
                {
                    message = $"This vessel cannot be deleted because it is linked to {linkedJobCount} survey job(s). Keep it active for report generation, or reassign those jobs to another vessel first."
                });
            }

            vessel.Status = VesselStatus.Inactive;
            vessel.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(ToDto(vessel));
        }

        [HttpDelete("{id:int}")]
        public Task<IActionResult> Delete(int id)
        {

            return Deactivate(id);
        }

        private VesselDto ToDto(Vessel vessel)
        {
            return new VesselDto
            {
                Id = vessel.Id,
                VesselName = vessel.VesselName,
                ImoNumber = vessel.ImoNumber,
                OfficialNumber = vessel.OfficialNumber,
                VesselType = vessel.VesselType,
                Flag = vessel.Flag,
                PortOfRegistry = vessel.PortOfRegistry,
                ClassSociety = vessel.ClassSociety,
                GrossTonnage = vessel.GrossTonnage,
                NetTonnage = vessel.NetTonnage,
                Deadweight = vessel.Deadweight,
                LengthOverall = vessel.LengthOverall,
                Breadth = vessel.Breadth,
                Depth = vessel.Depth,
                YearBuilt = vessel.YearBuilt,
                OwnerName = vessel.OwnerName,
                ManagerName = vessel.ManagerName,
                ClientName = vessel.ClientName,
                CallSign = vessel.CallSign,
                Builder = vessel.Builder,
                DateOfBuild = vessel.DateOfBuild,
                LengthBetweenPerpendiculars = vessel.LengthBetweenPerpendiculars,
                DraughtMoulded = vessel.DraughtMoulded,
                ProjectId = vessel.ProjectId,
                ProjectName = vessel.ProjectName,
                Status = vessel.Status.ToString(),
                CreatedByUserId = vessel.CreatedByUserId,
                CreatedAt = vessel.CreatedAt,
                UpdatedAt = vessel.UpdatedAt,
                CanEdit = CanEditVessel(vessel)
            };
        }

        private bool CanEditVessel(Vessel vessel)
        {
            if (_access.IsAdmin)
                return true;

            return _access.IsManager &&
                   _access.UserId.HasValue &&
                   vessel.CreatedByUserId == _access.UserId.Value;
        }
    }

    public class SaveVesselRequest
    {
        public int? ProjectId { get; set; }
        public string? ProjectName { get; set; }
        public string VesselName { get; set; } = string.Empty;
        public string? ImoNumber { get; set; }
        public string? OfficialNumber { get; set; }
        public string? VesselType { get; set; }
        public string? Flag { get; set; }
        public string? PortOfRegistry { get; set; }
        public string? ClassSociety { get; set; }
        public decimal? GrossTonnage { get; set; }
        public decimal? NetTonnage { get; set; }
        public decimal? Deadweight { get; set; }
        public decimal? LengthOverall { get; set; }
        public decimal? Breadth { get; set; }
        public decimal? Depth { get; set; }
        public int? YearBuilt { get; set; }
        public string? OwnerName { get; set; }
        public string? ManagerName { get; set; }
        public string? ClientName { get; set; }
        public string? CallSign { get; set; }
        public string? Builder { get; set; }
        public DateTime? DateOfBuild { get; set; }
        public decimal? LengthBetweenPerpendiculars { get; set; }
        public decimal? DraughtMoulded { get; set; }
    }

    public class VesselDto : SaveVesselRequest
    {
        public int Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public int? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool CanEdit { get; set; }
    }
}
