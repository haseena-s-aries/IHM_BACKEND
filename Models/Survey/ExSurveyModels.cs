using System.ComponentModel.DataAnnotations;

namespace Survey_Certification_App.Models.Survey
{
    public enum ExChecklistResult
    {
        Pass = 1,
        Fail = 2,
        NA = 3
    }

    public enum ExDefectStatus
    {
        Open = 1,
        Closed = 2,
        Accepted = 3
    }

    public class ExEquipment
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }

        [Required]
        [MaxLength(100)]
        public string EquipmentId { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? RfidTag { get; set; }

        [MaxLength(200)]
        public string? Area { get; set; }

        [MaxLength(200)]
        public string? Location { get; set; }

        [MaxLength(200)]
        public string? EquipmentType { get; set; }

        [MaxLength(300)]
        public string? Description { get; set; }

        [MaxLength(100)]
        public string? Manufacturer { get; set; }

        [MaxLength(100)]
        public string? Model { get; set; }

        [MaxLength(100)]
        public string? SerialNumber { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public SurveyJob? SurveyJob { get; set; }
    }

    public class ExChecklistQuestion
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string QuestionCode { get; set; } = string.Empty;

        [MaxLength(150)]
        public string? Section { get; set; }

        [Required]
        [MaxLength(1000)]
        public string QuestionText { get; set; } = string.Empty;

        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class ExChecklistAnswer
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }

        public int ExEquipmentId { get; set; }

        public int ExChecklistQuestionId { get; set; }

        public ExChecklistResult Result { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }

        public int AnsweredByUserId { get; set; }

        public DateTime AnsweredAt { get; set; } = DateTime.UtcNow;

        public SurveyJob? SurveyJob { get; set; }

        public ExEquipment? ExEquipment { get; set; }

        public ExChecklistQuestion? ExChecklistQuestion { get; set; }
    }

    public class ExDefect
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }

        public int ExEquipmentId { get; set; }

        [MaxLength(100)]
        public string? DefectNumber { get; set; }

        [Required]
        [MaxLength(1000)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Severity { get; set; }

        [MaxLength(1000)]
        public string? Recommendation { get; set; }

        public ExDefectStatus Status { get; set; } = ExDefectStatus.Open;

        public int CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ClosedAt { get; set; }

        public SurveyJob? SurveyJob { get; set; }

        public ExEquipment? ExEquipment { get; set; }
    }
}
