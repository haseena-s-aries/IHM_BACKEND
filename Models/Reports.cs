using Survey_Certification_App.Models.Survey;

public class IhmReportInfo
{
    public int Id { get; set; }
    public int SurveyJobId { get; set; }

    public string? CompanyName { get; set; }
    public string? CompanyAddress { get; set; }
    public string? CompanyTel { get; set; }
    public string? CompanyFax { get; set; }
    public string? CompanyWebsite { get; set; }
    public string? CompanyEmail { get; set; }
    public string? CompanyLogoPath { get; set; }

    public string? ClientName { get; set; }
    public string? ClientLogoPath { get; set; }

    public string? PreparedBy { get; set; }
    public string? CheckedBy { get; set; }
    public string? ApprovedBy { get; set; }

    public string? Uid { get; set; }
    public string? DocumentNo { get; set; }
    public DateTime? DocumentDate { get; set; }
    public string? RevisionNo { get; set; }
    public string? LabReportNo { get; set; }
    public DateTime? LabReportingDate { get; set; }
    public string? HazardousMaterialNoFindings { get; set; }
    public string? HazardousMaterialFindings { get; set; }
    public string? ShipManagerContact { get; set; }
    public string? HazmatExpert { get; set; }
    public string? SamplingMethodology { get; set; }
    public string? SamplingOfFragmentsOfPaints { get; set; }
    public string? Laboratory { get; set; }
    public string? ChemicalAnalysisMethod { get; set; }
    public string? LocationOfVisualSamplingCheck { get; set; }
    public string? OhsJobDetail { get; set; }
    public string? OhsClient { get; set; }
    public string? OhsJobLocation { get; set; }
    public string? OhsJobNo { get; set; }
    public string? OhsProjectInCharge { get; set; }
    public string? OhsDivisionBranch { get; set; }
    public string? OhsRaNo { get; set; }
    public string? OhsRevNo { get; set; }
    public DateTime? OhsRevisionDate { get; set; }
    public string? OhsPreparedBy { get; set; }
    public string? OhsPreparedBySignPath { get; set; }
    public DateTime? OhsPreparedDate { get; set; }

    public string? OhsApprovedBy { get; set; }
    public string? OhsApprovedBySignPath { get; set; }
    public DateTime? OhsApprovedDate { get; set; }
    public SurveyJob? SurveyJob { get; set; }
}

public class IhmReportRevision

{

    public int Id { get; set; }

    public int SurveyJobId { get; set; }

    public string? Uid { get; set; }

    public string? RevisionNo { get; set; }

    public DateTime? RevisionDate { get; set; }

    public string? Description { get; set; }

    public string? PreparedBy { get; set; }

    public string? CheckedBy { get; set; }

    public string? ApprovedBy { get; set; }

    public SurveyJob? SurveyJob { get; set; }

}


public class IhmPaintCoatingSystem
{
    public int Id { get; set; }
    public int SurveyJobId { get; set; }

    public string? ApplicationOfPaint { get; set; }
    public string? NameOfPaint { get; set; }
    public string? Location { get; set; }
    public string? MaterialClassification { get; set; }

    public string? Mass { get; set; }
    public string? Unit { get; set; }

    public string? Remarks { get; set; }

    public SurveyJob? SurveyJob { get; set; }
}

public class IhmEquipmentMachineryReportRow
{
    public int Id { get; set; }
    public int SurveyJobId { get; set; }

    public int? IhmCheckpointId { get; set; }

    public string? EquipmentName { get; set; }
    public string? Location { get; set; }
    public string? MaterialClassification { get; set; }
    public string? PartsWereUsed { get; set; }

    public string? UnitKg { get; set; }
    public string? QuantityNo { get; set; }
    public string? TotalKg { get; set; }

    public string? Remarks { get; set; }

    public int SortOrder { get; set; }
    public bool IsHidden { get; set; }
    public SurveyJob? SurveyJob { get; set; }
    public IhmCheckpoint? IhmCheckpoint { get; set; }
}
public class IhmStructureHull
{
    public int Id { get; set; }
    public int SurveyJobId { get; set; }

    public string? EquipmentName { get; set; }
    public string? Location { get; set; }
    public string? MaterialClassification { get; set; }
    public string? PartsWereUsed { get; set; }
    public string? ApproximateQuantity { get; set; }
    public string? Remarks { get; set; }

    public SurveyJob? SurveyJob { get; set; }
}

public class IhmNecessaryInformationPoint
{
    public int Id { get; set; }
    public int SurveyJobId { get; set; }

    public string? Description { get; set; }
    public int SortOrder { get; set; }

    public SurveyJob? SurveyJob { get; set; }
}
public class IhmOhsPerson
{
    public int Id { get; set; }
    public int SurveyJobId { get; set; }

    public string RoleType { get; set; } = string.Empty;
    // "HseOfficer", "ResponsibleSupervisor", "Participant"

    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public SurveyJob? SurveyJob { get; set; }
}

public class IhmOhsRiskAssessmentRow
{
    public int Id { get; set; }
    public int SurveyJobId { get; set; }

    public string? Activity { get; set; }
    public string? HazardDescription { get; set; }
    public string? Risks { get; set; }

    public string? InitialRiskP { get; set; }
    public string? InitialRiskS { get; set; }
    public string? InitialRiskR { get; set; }
    public string? InitialAlarp { get; set; }

    // Store bullet points separated by new line
    public string? ControlMeasures { get; set; }

    public string? ResidualRiskP { get; set; }
    public string? ResidualRiskS { get; set; }
    public string? ResidualRiskR { get; set; }
    public string? ResidualAlarp { get; set; }

    public string? ResponsiblePerson { get; set; }
    public int SortOrder { get; set; }

    public SurveyJob? SurveyJob { get; set; }
}

public class IhmAppendixOneResultPoint
{
    public int Id { get; set; }
    public int SurveyJobId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public SurveyJob? SurveyJob { get; set; }
}

public class IhmAppendixFiveInformationPoint
{
    public int Id { get; set; }
    public int SurveyJobId { get; set; }

    public string? Description { get; set; }
    public int SortOrder { get; set; }

    public SurveyJob? SurveyJob { get; set; }
}
public class IhmAppendixTwoAttachment
{
    public int Id { get; set; }
    public int SurveyJobId { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public string AppendixType { get; set; } = "AppendixTwo";
    public int? AppendixNo { get; set; }
    public string? AppendixTitle { get; set; }
    public int SortOrder { get; set; }

    public SurveyJob? SurveyJob { get; set; }
}