using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Survey_Certification_App.Models.Survey;
namespace Survey_Certification_App.Models
{
    public enum VesselStatus
    {
        Active = 1,
        Inactive = 2
    }

    public class Vessel
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string VesselName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? ImoNumber { get; set; }

        [MaxLength(100)]
        public string? OfficialNumber { get; set; }

        [MaxLength(100)]
        public string? VesselType { get; set; }

        [MaxLength(100)]
        public string? Flag { get; set; }

        [MaxLength(150)]
        public string? PortOfRegistry { get; set; }

        [MaxLength(150)]
        public string? ClassSociety { get; set; }
        [Column(TypeName = "decimal(18,4)")]
        public decimal? GrossTonnage { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? NetTonnage { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? Deadweight { get; set; }
        [Column(TypeName = "decimal(18,4)")]
        public decimal? LengthOverall { get; set; }
        [Column(TypeName = "decimal(18,4)")]
        public decimal? Breadth { get; set; }
        [Column(TypeName = "decimal(18,4)")]
        public decimal? Depth { get; set; }

        public int? YearBuilt { get; set; }

        [MaxLength(200)]
        public string? OwnerName { get; set; }

        [MaxLength(200)]
        public string? ManagerName { get; set; }

        [MaxLength(200)]
        public string? ClientName { get; set; }

        public VesselStatus Status { get; set; } = VesselStatus.Active;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public string? CallSign { get; set; }

        public string? Builder { get; set; }

        public DateTime? DateOfBuild { get; set; }
        [Precision(18, 4)]
        public decimal? LengthBetweenPerpendiculars { get; set; }
        [Precision(18, 4)]
        public decimal? DraughtMoulded { get; set; }

        public ICollection<SurveyJob> SurveyJobs { get; set; } = new List<SurveyJob>();
    }
}
