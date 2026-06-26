using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Survey_Certification_App.Data;
using Survey_Certification_App.Models;

namespace Survey_Certification_App.Controllers
{
    [Route("api/vessels")]
    [ApiController]
    public class VesselController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public VesselController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var vessels = await _db.Vessels
                .Where(x => x.Status == VesselStatus.Active)
                .OrderBy(x => x.VesselName)
                .Select(x => ToDto(x))
                .ToListAsync();

            return Ok(vessels);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var vessel = await _db.Vessels.FindAsync(id);

            if (vessel == null)
                return NotFound("Vessel not found.");

            return Ok(ToDto(vessel));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveVesselRequest request)
        {
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
            };

            _db.Vessels.Add(vessel);
            await _db.SaveChangesAsync();

            return Ok(ToDto(vessel));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SaveVesselRequest request)
        {
            var vessel = await _db.Vessels.FindAsync(id);

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
            var vessel = await _db.Vessels.FindAsync(id);

            if (vessel == null)
                return NotFound("Vessel not found.");

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

        private static VesselDto ToDto(Vessel vessel)
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
                Status = vessel.Status.ToString()
            };
        }
    }

    public class SaveVesselRequest
    {
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
    }
}
