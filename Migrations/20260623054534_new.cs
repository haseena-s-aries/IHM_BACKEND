using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Survey_Certification_App.Migrations
{
    /// <inheritdoc />
    public partial class @new : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "certificates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HeaderImagePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CertificateType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JobNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProjectTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CertificateNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClientName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Cargo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeparturePort = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ArrivalPort = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IssuedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NoDateCheckbox = table.Column<bool>(type: "bit", nullable: false),
                    PdfFilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SurveyedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TowName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TugName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StampPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SignaturePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SignatureClient = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsCargoDetailsNA = table.Column<bool>(type: "bit", nullable: false),
                    IncludeAnnexure2 = table.Column<bool>(type: "bit", nullable: false),
                    IncludeCargoDetails = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_certificates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExChecklistQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuestionCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Section = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    QuestionText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExChecklistQuestions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IhmCompanies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Country = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CityLocation = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    FullAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Tel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Fax = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Website = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    LogoPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmCompanies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "loadoutCertificates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HeaderImagePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CertificateTitle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OperationType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    JobNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ProjectTitle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CertificateNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Client = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Cargo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Modeofoperation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BargeVessel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IssuedBy = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IssuedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PdfFilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NoDateCheckbox = table.Column<bool>(type: "bit", nullable: false),
                    StampPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SignaturePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SignatureClient = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IncludeCargoDetails = table.Column<bool>(type: "bit", nullable: false),
                    IsCargoDetailsNA = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loadoutCertificates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "otpVerifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OtpCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiryTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_otpVerifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PdfDynamicEdits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    EditType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    X = table.Column<double>(type: "float", nullable: false),
                    Y = table.Column<double>(type: "float", nullable: false),
                    Width = table.Column<double>(type: "float", nullable: false),
                    Height = table.Column<double>(type: "float", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TableDataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FontSize = table.Column<float>(type: "real", nullable: false),
                    FontWeight = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Alignment = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AutoBullet = table.Column<bool>(type: "bit", nullable: false),
                    CoverOriginal = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PdfDynamicEdits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "recommendationModels",
                columns: table => new
                {
                    SerialNo = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendationModels", x => x.SerialNo);
                });

            migrationBuilder.CreateTable(
                name: "SeaFastenings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HeaderImagePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CertificateTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IssuedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Client = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TransportUnit = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProjectTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FromPort = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ToPort = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CargoType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JobNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CertificateNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IssuedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PdfFilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SlNo = table.Column<int>(type: "int", nullable: false),
                    StampPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SignaturePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SignatureClient = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsCargoDetailsNA = table.Column<bool>(type: "bit", nullable: false),
                    IncludeCargoDetails = table.Column<bool>(type: "bit", nullable: false),
                    Cargo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SerialNo = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IDMarkings = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Weight = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Dimensions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IncludeAnnexure2 = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeaFastenings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SurveyJobId = table.Column<int>(type: "int", nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EmailNormalized = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Username = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UsernameNormalized = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestedRole = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    EmployeeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FieldName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsOtpVerified = table.Column<bool>(type: "bit", nullable: false),
                    OTP = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OTPExpiration = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedBy = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vessels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VesselName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ImoNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OfficialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    VesselType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Flag = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PortOfRegistry = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ClassSociety = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    GrossTonnage = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    NetTonnage = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Deadweight = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    LengthOverall = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Breadth = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Depth = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    YearBuilt = table.Column<int>(type: "int", nullable: true),
                    OwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ManagerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CallSign = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Builder = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateOfBuild = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LengthBetweenPerpendiculars = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    DraughtMoulded = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vessels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CargoDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SerialNo = table.Column<int>(type: "int", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Cargos = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IDMarkings = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Weight = table.Column<string>(type: "nvarchar(max)", precision: 18, scale: 2, nullable: true),
                    Dimensions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CertificateId = table.Column<int>(type: "int", nullable: true),
                    LoadoutCertificateId = table.Column<int>(type: "int", nullable: true),
                    SeaFasteningId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CargoDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CargoDetails_SeaFastenings_SeaFasteningId",
                        column: x => x.SeaFasteningId,
                        principalTable: "SeaFastenings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CargoDetails_certificates_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "certificates",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CargoDetails_loadoutCertificates_LoadoutCertificateId",
                        column: x => x.LoadoutCertificateId,
                        principalTable: "loadoutCertificates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "recommendations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecommendationText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SeaFasteningId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recommendations_SeaFastenings_SeaFasteningId",
                        column: x => x.SeaFasteningId,
                        principalTable: "SeaFastenings",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SurveyJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModuleType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JobNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    VesselId = table.Column<int>(type: "int", nullable: true),
                    VesselSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProjectName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VesselName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ImoNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SiteName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AuditPort = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AuditAddress = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurveyJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SurveyJobs_Vessels_VesselId",
                        column: x => x.VesselId,
                        principalTable: "Vessels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExEquipments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RfidTag = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Area = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EquipmentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Manufacturer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExEquipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExEquipments_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IhmAppendixFiveInformationPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmAppendixFiveInformationPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmAppendixFiveInformationPoints_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IhmAppendixOneResultPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmAppendixOneResultPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmAppendixOneResultPoints_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IhmAppendixTwoAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AppendixType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AppendixNo = table.Column<int>(type: "int", nullable: true),
                    AppendixTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmAppendixTwoAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmAppendixTwoAttachments_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IhmAreas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    AreaName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmAreas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmAreas_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IhmNecessaryInformationPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmNecessaryInformationPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmNecessaryInformationPoints_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IhmOhsPeople",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    RoleType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmOhsPeople", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmOhsPeople_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IhmOhsRiskAssessmentRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    Activity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HazardDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Risks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InitialRiskP = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InitialRiskS = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InitialRiskR = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InitialAlarp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ControlMeasures = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResidualRiskP = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResidualRiskS = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResidualRiskR = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResidualAlarp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponsiblePerson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmOhsRiskAssessmentRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmOhsRiskAssessmentRows_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IhmPaintCoatingSystems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    ApplicationOfPaint = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NameOfPaint = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MaterialClassification = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Mass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmPaintCoatingSystems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmPaintCoatingSystems_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IhmReportInfos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompanyAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompanyTel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompanyFax = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompanyWebsite = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompanyEmail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompanyLogoPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientLogoPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreparedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CheckedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Uid = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DocumentNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DocumentDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevisionNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LabReportNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LabReportingDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HazardousMaterialFindings = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShipManagerContact = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HazmatExpert = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SamplingMethodology = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SamplingOfFragmentsOfPaints = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Laboratory = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChemicalAnalysisMethod = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LocationOfVisualSamplingCheck = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OhsJobDetail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OhsClient = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OhsJobLocation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OhsJobNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OhsProjectInCharge = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OhsDivisionBranch = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OhsRaNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OhsRevNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OhsRevisionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OhsPreparedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OhsPreparedBySignPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OhsPreparedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OhsApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OhsApprovedBySignPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OhsApprovedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmReportInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmReportInfos_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IhmReportRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    Uid = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RevisionNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RevisionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreparedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CheckedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmReportRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmReportRevisions_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IhmStructureHulls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    EquipmentName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MaterialClassification = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartsWereUsed = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApproximateQuantity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmStructureHulls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmStructureHulls_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SurveyAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    SurveyorUserId = table.Column<int>(type: "int", nullable: false),
                    AssignedByUserId = table.Column<int>(type: "int", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurveyAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SurveyAssignments_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SurveyPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    RelatedModule = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RelatedRecordId = table.Column<int>(type: "int", nullable: true),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Latitude = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurveyPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SurveyPhotos_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SurveyUploadedFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FileType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurveyUploadedFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SurveyUploadedFiles_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExChecklistAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    ExEquipmentId = table.Column<int>(type: "int", nullable: false),
                    ExChecklistQuestionId = table.Column<int>(type: "int", nullable: false),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AnsweredByUserId = table.Column<int>(type: "int", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExChecklistAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExChecklistAnswers_ExChecklistQuestions_ExChecklistQuestionId",
                        column: x => x.ExChecklistQuestionId,
                        principalTable: "ExChecklistQuestions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExChecklistAnswers_ExEquipments_ExEquipmentId",
                        column: x => x.ExEquipmentId,
                        principalTable: "ExEquipments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExChecklistAnswers_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExDefects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    ExEquipmentId = table.Column<int>(type: "int", nullable: false),
                    DefectNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Recommendation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExDefects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExDefects_ExEquipments_ExEquipmentId",
                        column: x => x.ExEquipmentId,
                        principalTable: "ExEquipments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExDefects_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IhmAreaLayoutPlacements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    IhmAreaId = table.Column<int>(type: "int", nullable: false),
                    XPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    YPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    WidthPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    HeightPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    MatchConfidence = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    IsAutoMatched = table.Column<bool>(type: "bit", nullable: false),
                    IsManuallyAdjusted = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmAreaLayoutPlacements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmAreaLayoutPlacements_IhmAreas_IhmAreaId",
                        column: x => x.IhmAreaId,
                        principalTable: "IhmAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IhmAreaLayoutPlacements_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IhmCheckpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    IhmAreaId = table.Column<int>(type: "int", nullable: false),
                    Sample_No = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Table_A_B = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    HazardousMaterial = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TableType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EquipmentName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Component = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QuantityUnitKg = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QuantityNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QuantityTotalKg = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPchm = table.Column<bool>(type: "bit", nullable: false),
                    LocationRemarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ManufacturerBrandName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DocumentAnalysisResult = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SamplingCheck = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VisualCheck = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CheckResult = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmCheckpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmCheckpoints_IhmAreas_IhmAreaId",
                        column: x => x.IhmAreaId,
                        principalTable: "IhmAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IhmCheckpoints_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IhmCheckpointSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    IhmCheckpointId = table.Column<int>(type: "int", nullable: false),
                    SurveyorUserId = table.Column<int>(type: "int", nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewNotes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmCheckpointSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmCheckpointSubmissions_IhmCheckpoints_IhmCheckpointId",
                        column: x => x.IhmCheckpointId,
                        principalTable: "IhmCheckpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IhmCheckpointSubmissions_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IhmEquipmentMachineryReportRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    IhmCheckpointId = table.Column<int>(type: "int", nullable: true),
                    EquipmentName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MaterialClassification = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartsWereUsed = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UnitKg = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QuantityNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TotalKg = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmEquipmentMachineryReportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmEquipmentMachineryReportRows_IhmCheckpoints_IhmCheckpointId",
                        column: x => x.IhmCheckpointId,
                        principalTable: "IhmCheckpoints",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IhmEquipmentMachineryReportRows_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IhmImageMarkers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    IhmAreaId = table.Column<int>(type: "int", nullable: true),
                    IhmCheckpointId = table.Column<int>(type: "int", nullable: true),
                    ImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    XPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    YPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    MarkerLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmImageMarkers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmImageMarkers_IhmAreas_IhmAreaId",
                        column: x => x.IhmAreaId,
                        principalTable: "IhmAreas",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IhmImageMarkers_IhmCheckpoints_IhmCheckpointId",
                        column: x => x.IhmCheckpointId,
                        principalTable: "IhmCheckpoints",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IhmImageMarkers_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IhmSamplingPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyJobId = table.Column<int>(type: "int", nullable: false),
                    IhmAreaId = table.Column<int>(type: "int", nullable: true),
                    IhmCheckpointId = table.Column<int>(type: "int", nullable: true),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Caption = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IhmSamplingPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IhmSamplingPhotos_IhmAreas_IhmAreaId",
                        column: x => x.IhmAreaId,
                        principalTable: "IhmAreas",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IhmSamplingPhotos_IhmCheckpoints_IhmCheckpointId",
                        column: x => x.IhmCheckpointId,
                        principalTable: "IhmCheckpoints",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IhmSamplingPhotos_SurveyJobs_SurveyJobId",
                        column: x => x.SurveyJobId,
                        principalTable: "SurveyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CargoDetails_CertificateId",
                table: "CargoDetails",
                column: "CertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_CargoDetails_LoadoutCertificateId",
                table: "CargoDetails",
                column: "LoadoutCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_CargoDetails_SeaFasteningId",
                table: "CargoDetails",
                column: "SeaFasteningId");

            migrationBuilder.CreateIndex(
                name: "IX_ExChecklistAnswers_ExChecklistQuestionId",
                table: "ExChecklistAnswers",
                column: "ExChecklistQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExChecklistAnswers_ExEquipmentId",
                table: "ExChecklistAnswers",
                column: "ExEquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExChecklistAnswers_SurveyJobId",
                table: "ExChecklistAnswers",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ExDefects_ExEquipmentId",
                table: "ExDefects",
                column: "ExEquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExDefects_SurveyJobId",
                table: "ExDefects",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ExEquipments_SurveyJobId",
                table: "ExEquipments",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmAppendixFiveInformationPoints_SurveyJobId",
                table: "IhmAppendixFiveInformationPoints",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmAppendixOneResultPoints_SurveyJobId",
                table: "IhmAppendixOneResultPoints",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmAppendixTwoAttachments_SurveyJobId",
                table: "IhmAppendixTwoAttachments",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmAreaLayoutPlacements_IhmAreaId",
                table: "IhmAreaLayoutPlacements",
                column: "IhmAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmAreaLayoutPlacements_SurveyJobId",
                table: "IhmAreaLayoutPlacements",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmAreas_SurveyJobId",
                table: "IhmAreas",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmCheckpoints_IhmAreaId",
                table: "IhmCheckpoints",
                column: "IhmAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmCheckpoints_SurveyJobId",
                table: "IhmCheckpoints",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmCheckpointSubmissions_IhmCheckpointId_SurveyorUserId",
                table: "IhmCheckpointSubmissions",
                columns: new[] { "IhmCheckpointId", "SurveyorUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IhmCheckpointSubmissions_SurveyJobId",
                table: "IhmCheckpointSubmissions",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmCompanies_CompanyName",
                table: "IhmCompanies",
                column: "CompanyName");

            migrationBuilder.CreateIndex(
                name: "IX_IhmEquipmentMachineryReportRows_IhmCheckpointId",
                table: "IhmEquipmentMachineryReportRows",
                column: "IhmCheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmEquipmentMachineryReportRows_SurveyJobId",
                table: "IhmEquipmentMachineryReportRows",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmImageMarkers_IhmAreaId",
                table: "IhmImageMarkers",
                column: "IhmAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmImageMarkers_IhmCheckpointId",
                table: "IhmImageMarkers",
                column: "IhmCheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmImageMarkers_SurveyJobId",
                table: "IhmImageMarkers",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmNecessaryInformationPoints_SurveyJobId",
                table: "IhmNecessaryInformationPoints",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmOhsPeople_SurveyJobId",
                table: "IhmOhsPeople",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmOhsRiskAssessmentRows_SurveyJobId",
                table: "IhmOhsRiskAssessmentRows",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmPaintCoatingSystems_SurveyJobId",
                table: "IhmPaintCoatingSystems",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmReportInfos_SurveyJobId",
                table: "IhmReportInfos",
                column: "SurveyJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IhmReportRevisions_SurveyJobId",
                table: "IhmReportRevisions",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmSamplingPhotos_IhmAreaId",
                table: "IhmSamplingPhotos",
                column: "IhmAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmSamplingPhotos_IhmCheckpointId",
                table: "IhmSamplingPhotos",
                column: "IhmCheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmSamplingPhotos_SurveyJobId",
                table: "IhmSamplingPhotos",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_IhmStructureHulls_SurveyJobId",
                table: "IhmStructureHulls",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_PdfDynamicEdits_ReportType_ReportId",
                table: "PdfDynamicEdits",
                columns: new[] { "ReportType", "ReportId" });

            migrationBuilder.CreateIndex(
                name: "IX_recommendations_SeaFasteningId",
                table: "recommendations",
                column: "SeaFasteningId");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyAssignments_SurveyJobId",
                table: "SurveyAssignments",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyJobs_VesselId",
                table: "SurveyJobs",
                column: "VesselId");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyPhotos_SurveyJobId",
                table: "SurveyPhotos",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyUploadedFiles_SurveyJobId",
                table: "SurveyUploadedFiles",
                column: "SurveyJobId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_UserId_IsRead_CreatedAt",
                table: "UserNotifications",
                columns: new[] { "UserId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Vessels_ImoNumber",
                table: "Vessels",
                column: "ImoNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CargoDetails");

            migrationBuilder.DropTable(
                name: "ExChecklistAnswers");

            migrationBuilder.DropTable(
                name: "ExDefects");

            migrationBuilder.DropTable(
                name: "IhmAppendixFiveInformationPoints");

            migrationBuilder.DropTable(
                name: "IhmAppendixOneResultPoints");

            migrationBuilder.DropTable(
                name: "IhmAppendixTwoAttachments");

            migrationBuilder.DropTable(
                name: "IhmAreaLayoutPlacements");

            migrationBuilder.DropTable(
                name: "IhmCheckpointSubmissions");

            migrationBuilder.DropTable(
                name: "IhmCompanies");

            migrationBuilder.DropTable(
                name: "IhmEquipmentMachineryReportRows");

            migrationBuilder.DropTable(
                name: "IhmImageMarkers");

            migrationBuilder.DropTable(
                name: "IhmNecessaryInformationPoints");

            migrationBuilder.DropTable(
                name: "IhmOhsPeople");

            migrationBuilder.DropTable(
                name: "IhmOhsRiskAssessmentRows");

            migrationBuilder.DropTable(
                name: "IhmPaintCoatingSystems");

            migrationBuilder.DropTable(
                name: "IhmReportInfos");

            migrationBuilder.DropTable(
                name: "IhmReportRevisions");

            migrationBuilder.DropTable(
                name: "IhmSamplingPhotos");

            migrationBuilder.DropTable(
                name: "IhmStructureHulls");

            migrationBuilder.DropTable(
                name: "otpVerifications");

            migrationBuilder.DropTable(
                name: "PdfDynamicEdits");

            migrationBuilder.DropTable(
                name: "recommendationModels");

            migrationBuilder.DropTable(
                name: "recommendations");

            migrationBuilder.DropTable(
                name: "SurveyAssignments");

            migrationBuilder.DropTable(
                name: "SurveyPhotos");

            migrationBuilder.DropTable(
                name: "SurveyUploadedFiles");

            migrationBuilder.DropTable(
                name: "UserNotifications");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "certificates");

            migrationBuilder.DropTable(
                name: "loadoutCertificates");

            migrationBuilder.DropTable(
                name: "ExChecklistQuestions");

            migrationBuilder.DropTable(
                name: "ExEquipments");

            migrationBuilder.DropTable(
                name: "IhmCheckpoints");

            migrationBuilder.DropTable(
                name: "SeaFastenings");

            migrationBuilder.DropTable(
                name: "IhmAreas");

            migrationBuilder.DropTable(
                name: "SurveyJobs");

            migrationBuilder.DropTable(
                name: "Vessels");
        }
    }
}
