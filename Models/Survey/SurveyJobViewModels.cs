using System.ComponentModel.DataAnnotations;

namespace Survery_Certification_App_Consume.Models.Survey
{
    public class CreateSurveyJobViewModel
    {
        [Required]
        public string JobNumber { get; set; } = string.Empty;

        public string? ClientName { get; set; }

        public string? ProjectName { get; set; }

        public string? VesselName { get; set; }

        public string? ImoNumber { get; set; }

        public string? SiteName { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }
    }

    public class SurveyJobListItemViewModel
    {
        public int Id { get; set; }

        public string ModuleType { get; set; } = string.Empty;

        public string JobNumber { get; set; } = string.Empty;

        public string? ClientName { get; set; }

        public string? VesselName { get; set; }
        public string? ImoNumber { get; set; }
        public string? SiteName { get; set; }

        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }

    public class SurveyJobDetailsViewModel
    {
        public int Id { get; set; }

        public string ModuleType { get; set; } = string.Empty;

        public string JobNumber { get; set; } = string.Empty;

        public string? ClientName { get; set; }

        public string? ProjectName { get; set; }

        public string? VesselName { get; set; }

        public string? ImoNumber { get; set; }

        public string? SiteName { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public string Status { get; set; } = string.Empty;

        public int CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public List<int> AssignedSurveyorIds { get; set; } = new();
    }

    public class AssignSurveyorViewModel
    {
        public int SurveyJobId { get; set; }
        public int SurveyorUserId { get; set; }
    }

    public class SurveyJobDetailsPageViewModel
    {
        public SurveyJobDetailsViewModel Job { get; set; } = new();

        public AssignSurveyorViewModel Assignment { get; set; } = new();
    }

    public class ExEquipmentViewModel
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }

        public string EquipmentId { get; set; } = string.Empty;

        public string? RfidTag { get; set; }

        public string? Area { get; set; }

        public string? Location { get; set; }

        public string? EquipmentType { get; set; }

        public string? Description { get; set; }

        public string? Manufacturer { get; set; }

        public string? Model { get; set; }

        public string? SerialNumber { get; set; }

        public bool IsActive { get; set; }
    }

    public class CreateExEquipmentViewModel
    {
        public int SurveyJobId { get; set; }

        public string EquipmentId { get; set; } = string.Empty;

        public string? RfidTag { get; set; }

        public string? Area { get; set; }

        public string? Location { get; set; }

        public string? EquipmentType { get; set; }

        public string? Description { get; set; }

        public string? Manufacturer { get; set; }

        public string? Model { get; set; }

        public string? SerialNumber { get; set; }
    }

    public class ExEquipmentRegisterPageViewModel
    {
        public int SurveyJobId { get; set; }

        public List<ExEquipmentViewModel> Equipment { get; set; } = new();

        public CreateExEquipmentViewModel NewEquipment { get; set; } = new();
    }

    public class ExDefectViewModel
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }

        public int ExEquipmentId { get; set; }

        public string? EquipmentId { get; set; }

        public string? DefectNumber { get; set; }

        public string Description { get; set; } = string.Empty;

        public string? Severity { get; set; }

        public string? Recommendation { get; set; }

        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }
    public class ExChecklistQuestionViewModel
    {
        public int Id { get; set; }

        public string QuestionCode { get; set; } = string.Empty;

        public string? Section { get; set; }

        public string QuestionText { get; set; } = string.Empty;

        public int SortOrder { get; set; }
    }

    public class CreateExChecklistQuestionViewModel
    {
        public int SurveyJobId { get; set; }

        public string QuestionCode { get; set; } = string.Empty;

        public string? Section { get; set; }

        public string QuestionText { get; set; } = string.Empty;

        public int SortOrder { get; set; }
    }

    public class ExChecklistQuestionPageViewModel
    {
        public int SurveyJobId { get; set; }

        public List<ExChecklistQuestionViewModel> Questions { get; set; } = new();

        public CreateExChecklistQuestionViewModel NewQuestion { get; set; } = new();
    }
    public class IhmAreaViewModel
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }

        public string AreaName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public int SortOrder { get; set; }
    }

    public class CreateIhmAreaViewModel
    {
        public int SurveyJobId { get; set; }

        public string AreaName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public int SortOrder { get; set; }
    }

    public class IhmAreaPageViewModel
    {
        public int SurveyJobId { get; set; }

        public List<IhmAreaViewModel> Areas { get; set; } = new();

        public CreateIhmAreaViewModel NewArea { get; set; } = new();
    }

    public class IhmCheckpointViewModel
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }

        public int IhmAreaId { get; set; }

        public string CheckpointCode { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string? MaterialCategory { get; set; }

        public string? HazardousMaterial { get; set; }

        public string Result { get; set; } = string.Empty;

        public string? Remarks { get; set; }
    }

    public class CreateIhmCheckpointViewModel
    {
        public int SurveyJobId { get; set; }

        public int IhmAreaId { get; set; }

        public string CheckpointCode { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string? MaterialCategory { get; set; }

        public string? HazardousMaterial { get; set; }
    }

    public class IhmCheckpointPageViewModel
    {
        public int SurveyJobId { get; set; }

        public int IhmAreaId { get; set; }

        public List<IhmCheckpointViewModel> Checkpoints { get; set; } = new();

        public CreateIhmCheckpointViewModel NewCheckpoint { get; set; } = new();
    }
    public class SurveyPhotoViewModel
    {
        public int Id { get; set; }

        public int SurveyJobId { get; set; }

        public string? RelatedModule { get; set; }

        public int? RelatedRecordId { get; set; }

        public string OriginalFileName { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;

        public string? ContentType { get; set; }

        public int UploadedByUserId { get; set; }

        public DateTime UploadedAt { get; set; }
    }
    public class SurveySummaryViewModel
    {
        public int TotalSurveyJobs { get; set; }

        public int ExSurveyJobs { get; set; }

        public int IhmSurveyJobs { get; set; }

        public int SubmittedJobs { get; set; }

        public int ApprovedJobs { get; set; }

        public int ReturnedJobs { get; set; }
    }


}
