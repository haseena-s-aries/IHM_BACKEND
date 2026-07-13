using System.ComponentModel.DataAnnotations;

namespace Survey_Certification_App.Models
{
    public class ReportAssignment
    {
        public int Id { get; set; }

        [Required, MaxLength(40)]
        public string ReportType { get; set; } = string.Empty;

        public int ReportId { get; set; }

        public int? SurveyJobId { get; set; }

        public int ClientUserId { get; set; }

        public int AssignedByUserId { get; set; }

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;

        public DateTime? RevokedAt { get; set; }

        public int? RevokedByUserId { get; set; }

        public User? ClientUser { get; set; }

        public User? AssignedByUser { get; set; }

        public User? RevokedByUser { get; set; }
    }
}
