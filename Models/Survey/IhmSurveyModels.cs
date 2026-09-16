using System.ComponentModel.DataAnnotations;

namespace Survey_Certification_App.Models.Survey
{
    public enum IhmCheckpointResult
    {
        Pending = 1,
        Present = 2,
        NotPresent = 3,
        NotApplicable = 4,
        SampleRequired = 5
    }

    public enum IhmCheckpointSubmissionStatus
    {
        Draft = 1,
        Submitted = 2,
        Approved = 3,
        Rejected = 4,
        Conflict = 5
    }

    public enum IhmCheckpointAuditStatus
    {
        Pending = 1,
        InProgress = 2,
        Done = 3
    }

    public class IhmArea
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }

        [Required]
        [MaxLength(200)]
        public string AreaName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        public int SortOrder { get; set; }

        public SurveyJob? SurveyJob { get; set; }

        public ICollection<IhmCheckpoint> Checkpoints { get; set; } = new List<IhmCheckpoint>();
    }

    public class IhmCheckpoint
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }

        public int IhmAreaId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Sample_No { get; set; } = string.Empty;

        [Required]
        [MaxLength(1000)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Table_A_B { get; set; }

        [MaxLength(200)]
        public string? HazardousMaterial { get; set; }

        public IhmCheckpointResult Result { get; set; } = IhmCheckpointResult.Pending;

        public IhmCheckpointAuditStatus AuditStatus { get; set; } =
            IhmCheckpointAuditStatus.Pending;

        [MaxLength(1000)]
        public string? Remarks { get; set; }

        public int? UpdatedByUserId { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public SurveyJob? SurveyJob { get; set; }

        public IhmArea? IhmArea { get; set; }

        public string? TableType { get; set; }
        public string? Location { get; set; }
        public string? EquipmentName { get; set; }
        public string? Component { get; set; }

        public string? QuantityUnitKg { get; set; }
        public string? QuantityNo { get; set; }
        public string? QuantityTotalKg { get; set; }

        public bool IsPchm { get; set; }
        public string? LocationRemarks { get; set; }


        public string? ManufacturerBrandName { get; set; }
        public string? DocumentAnalysisResult { get; set; }
        public string? SamplingCheck { get; set; }
        public string? VisualCheck { get; set; }
        public string? CheckResult { get; set; }
        public int? SourceRowNumber { get; set; }

        public ICollection<IhmCheckpointSubmission> Submissions { get; set; } =
            new List<IhmCheckpointSubmission>();
    }

    public class IhmCheckpointSubmission
    {
        public int Id { get; set; }
        public int SurveyJobId { get; set; }
        public int IhmCheckpointId { get; set; }
        public int SurveyorUserId { get; set; }
        public string DataJson { get; set; } = "{}";
        public string DataHash { get; set; } = string.Empty;
        public IhmCheckpointSubmissionStatus Status { get; set; } =
            IhmCheckpointSubmissionStatus.Submitted;
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public int? ReviewedByUserId { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewNotes { get; set; }
        public IhmCheckpoint? IhmCheckpoint { get; set; }
        public SurveyJob? SurveyJob { get; set; }
    }

    public class IhmImageMarker
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }

        public int? IhmAreaId { get; set; }

        public int? IhmCheckpointId { get; set; }

        [MaxLength(500)]
        public string ImagePath { get; set; } = string.Empty;

        public decimal XPercent { get; set; }

        public decimal YPercent { get; set; }

        [MaxLength(100)]
        public string? MarkerLabel { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }
        [MaxLength(100)]
        public string? Source { get; set; }

        public bool SuppressRender { get; set; }

        public bool IsManualForExistingImage { get; set; }

        public int CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public SurveyJob? SurveyJob { get; set; }

        public IhmArea? IhmArea { get; set; }

        public IhmCheckpoint? IhmCheckpoint { get; set; }
    }

    public class IhmSamplingPhoto
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }

        public int? IhmAreaId { get; set; }

        public int? IhmCheckpointId { get; set; }

        [MaxLength(255)]
        public string OriginalFileName { get; set; } = string.Empty;

        [MaxLength(255)]
        public string StoredFileName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string FilePath { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? ContentType { get; set; }

        [MaxLength(500)]
        public string? Caption { get; set; }

        // JSON array of IHM area IDs assigned to this global machinery layout.
        [MaxLength(4000)]
        public string? AssignedAreaIds { get; set; }

        public int UploadedByUserId { get; set; }
        public bool ShowInAppendixThree { get; set; }
        public int AppendixThreeSortOrder { get; set; }
        public string? AppendixThreeEditedFilePath { get; set; }
        public int? AppendixThreeWidth { get; set; }
        public int? AppendixThreeHeight { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public SurveyJob? SurveyJob { get; set; }

        public IhmArea? IhmArea { get; set; }

        public IhmCheckpoint? IhmCheckpoint { get; set; }

    }
    public enum IhmMarkedImageSectionType
    {
        Hazardous = 1,
        Appendix4 = 2
    }

    public enum IhmMarkedImageStatus
    {
        Pending = 1,
        Processing = 2,
        Completed = 3,
        Failed = 4
    }

    public class IhmMarkedImageMetadata
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }

        public IhmMarkedImageSectionType SectionType { get; set; }

        [MaxLength(500)]
        public string SourceImagePath { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? GeneratedImagePath { get; set; }

        public DateTime? SourceImageLastModifiedUtc { get; set; }

        [MaxLength(128)]
        public string MarkerDataHash { get; set; } = string.Empty;

        public IhmMarkedImageStatus Status { get; set; } = IhmMarkedImageStatus.Pending;

        public int RetryCount { get; set; }

        public DateTime? GeneratedAtUtc { get; set; }

        public DateTime? LastQueuedAtUtc { get; set; }

        [MaxLength(2000)]
        public string? ErrorMessage { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public SurveyJob? SurveyJob { get; set; }
    }
    public class IhmAreaLayoutPlacement
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }
        public int IhmAreaId { get; set; }

        public decimal XPercent { get; set; }
        public decimal YPercent { get; set; }
        public decimal WidthPercent { get; set; }
        public decimal HeightPercent { get; set; }

        public decimal MatchConfidence { get; set; }
        public bool IsAutoMatched { get; set; }
        public bool IsManuallyAdjusted { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public SurveyJob? SurveyJob { get; set; }
        public IhmArea? IhmArea { get; set; }
    }
}
