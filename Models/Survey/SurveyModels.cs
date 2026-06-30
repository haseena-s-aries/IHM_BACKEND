using System.ComponentModel.DataAnnotations;

namespace Survey_Certification_App.Models.Survey
{
    public enum SurveyModuleType
    {
        ExSurvey = 1,
        IhmSurvey = 2
    }

    public enum SurveyJobStatus
    {
        Draft = 1,
        Assigned = 2,
        InProgress = 3,
        Submitted = 4,
        QcReturned = 5,
        Approved = 6,
        ReportGenerated = 7,
        Closed = 8
    }

    public enum SurveyAssignmentStatus
    {
        Assigned = 1,
        InProgress = 2,
        Completed = 3,
        Returned = 4
    }

    public class SurveyJob
    {
        public int Id { get; set; }

        [Required]
        public SurveyModuleType ModuleType { get; set; }

        [Required]
        [MaxLength(100)]
        public string JobNumber { get; set; } = string.Empty;
        public int? VesselId { get; set; }

        public string? VesselSnapshotJson { get; set; }
        [MaxLength(200)]
        public string? ClientName { get; set; }

        [MaxLength(200)]
        public string? ProjectName { get; set; }

        [MaxLength(200)]
        public string? VesselName { get; set; }

        [MaxLength(100)]
        public string? ImoNumber { get; set; }

        [MaxLength(200)]
        public string? SiteName { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? SurveyDate { get; set; }

        public DateTime? EndDate { get; set; }

        public SurveyJobStatus Status { get; set; } = SurveyJobStatus.Draft;

        public int CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public string? AuditPort { get; set; }
        public string? AuditAddress { get; set; }

        public Survey_Certification_App.Models.Vessel? Vessel { get; set; }
        public IhmReportInfo? IhmReportInfo { get; set; }
        public ICollection<SurveyAssignment> Assignments { get; set; } = new List<SurveyAssignment>();

        public ICollection<SurveyUploadedFile> UploadedFiles { get; set; } = new List<SurveyUploadedFile>();
        public ICollection<SurveyPhoto> Photos { get; set; } = new List<SurveyPhoto>();

    }

    public class SurveyAssignment
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }

        public int SurveyorUserId { get; set; }

        public int AssignedByUserId { get; set; }

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        public SurveyAssignmentStatus Status { get; set; } = SurveyAssignmentStatus.Assigned;

        public SurveyJob? SurveyJob { get; set; }
    }

    public class UserNotification
    {
        public int Id { get; set; }
        public int UserId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Type { get; set; } = "SurveyAssignment";

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(1000)]
        public string Message { get; set; } = string.Empty;

        public int? SurveyJobId { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReadAt { get; set; }
    }

    public class SurveyUploadedFile
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }

        [MaxLength(255)]
        public string OriginalFileName { get; set; } = string.Empty;

        [MaxLength(255)]
        public string StoredFileName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string FilePath { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? FileType { get; set; }

        [MaxLength(100)]
        public string? ContentType { get; set; }

        public long SizeBytes { get; set; }

        public int UploadedByUserId { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public SurveyJob? SurveyJob { get; set; }
    }

    public class SurveyPhoto
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }

        [MaxLength(100)]
        public string? RelatedModule { get; set; }

        public int? RelatedRecordId { get; set; }

        [MaxLength(255)]
        public string OriginalFileName { get; set; } = string.Empty;

        [MaxLength(255)]
        public string StoredFileName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string FilePath { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? ContentType { get; set; }

        public decimal? Latitude { get; set; }

        public decimal? Longitude { get; set; }

        public int UploadedByUserId { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public SurveyJob? SurveyJob { get; set; }
    }
}
