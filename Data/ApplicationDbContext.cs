using Microsoft.EntityFrameworkCore;
using Survey_Certification_App.Models;
using Survey_Certification_App.Models.Survey;
using Survey_Certification_App.Models.Access;

namespace Survey_Certification_App.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<User> users { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<ProjectRole> ProjectRoles { get; set; }
        public DbSet<ClientVesselMapping> ClientVesselMappings { get; set; }
        public DbSet<RecommendationModel> recommendationModels { get; set; }


        public DbSet<SeaFastening> SeaFastenings { get; set; }


        public DbSet<Recommendation> recommendations { get; set; }

        public DbSet<Certificate> certificates { get; set; }

        public DbSet<CargoDetails> CargoDetails { get; set; }

        public DbSet<OtpVerification> otpVerifications { get; set; }

        public DbSet<LoadoutCertificate> loadoutCertificates { get; set; }


        //public DbSet<ShipMainParticulars> Ships { get; set; }

        //public DbSet<CargoSpaceLayout> CargoLayouts { get; set; }

        public DbSet<SurveyJob> SurveyJobs { get; set; }
        public DbSet<SurveyAssignment> SurveyAssignments { get; set; }
        public DbSet<UserNotification> UserNotifications { get; set; }
        public DbSet<SurveyUploadedFile> SurveyUploadedFiles { get; set; }
        public DbSet<SurveyPhoto> SurveyPhotos { get; set; }
        public DbSet<ExEquipment> ExEquipments { get; set; }
        public DbSet<ExChecklistQuestion> ExChecklistQuestions { get; set; }
        public DbSet<ExChecklistAnswer> ExChecklistAnswers { get; set; }
        public DbSet<ExDefect> ExDefects { get; set; }
        public DbSet<IhmArea> IhmAreas { get; set; }
        public DbSet<IhmCheckpoint> IhmCheckpoints { get; set; }
        public DbSet<IhmCheckpointSubmission> IhmCheckpointSubmissions { get; set; }
        public DbSet<IhmImageMarker> IhmImageMarkers { get; set; }
        public DbSet<IhmSamplingPhoto> IhmSamplingPhotos { get; set; }
        public DbSet<Vessel> Vessels { get; set; }

        public DbSet<IhmReportInfo> IhmReportInfos { get; set; }
        public DbSet<IhmReportRevision> IhmReportRevisions { get; set; }
        public DbSet<IhmPaintCoatingSystem> IhmPaintCoatingSystems { get; set; }
        public DbSet<IhmEquipmentMachineryReportRow> IhmEquipmentMachineryReportRows { get; set; }
        public DbSet<IhmStructureHull> IhmStructureHulls { get; set; }
        public DbSet<IhmNecessaryInformationPoint> IhmNecessaryInformationPoints { get; set; }
        public DbSet<IhmAppendixFiveInformationPoint> IhmAppendixFiveInformationPoints { get; set; }
        public DbSet<IhmOhsPerson> IhmOhsPeople { get; set; }
        public DbSet<IhmOhsRiskAssessmentRow> IhmOhsRiskAssessmentRows { get; set; }
        public DbSet<IhmAppendixOneResultPoint> IhmAppendixOneResultPoints { get; set; }
        public DbSet<IhmAppendixTwoAttachment> IhmAppendixTwoAttachments { get; set; }
        public DbSet<IhmAreaLayoutPlacement> IhmAreaLayoutPlacements { get; set; }
        public DbSet<IhmMarkedImageMetadata> IhmMarkedImageMetadata { get; set; }
        public DbSet<PdfDynamicEdit> PdfDynamicEdits { get; set; }
        public DbSet<IhmCompany> IhmCompanies { get; set; }
        public DbSet<ReportAssignment> ReportAssignments { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)


        {


            modelBuilder.Entity<IhmCompany>()
    .HasIndex(x => x.CompanyName);
            modelBuilder.Entity<IhmCompany>()
    .HasIndex(x => new { x.CompanyName, x.DisplayName, x.Email });
            modelBuilder.Entity<IhmMarkedImageMetadata>()
    .HasIndex(x => new
    {
        x.SurveyJobId,
        x.SectionType,
        x.SourceImagePath
    });

            modelBuilder.Entity<IhmMarkedImageMetadata>()
                .Property(x => x.SectionType)
                .HasConversion<string>();

            modelBuilder.Entity<IhmMarkedImageMetadata>()
                .Property(x => x.Status)
                .HasConversion<string>();
            modelBuilder.Entity<PdfDynamicEdit>()
                .HasIndex(x => new { x.ReportType, x.ReportId });

            modelBuilder.Entity<PdfDynamicEdit>()
                .Property(x => x.Content)
                .HasColumnType("nvarchar(max)");

            modelBuilder.Entity<PdfDynamicEdit>()
                .Property(x => x.TableDataJson)
                .HasColumnType("nvarchar(max)");
            modelBuilder.Entity<CargoDetails>()
                .Property(c => c.Weight)
                .HasPrecision(18, 2); // Adjust precision and scale as needed
            modelBuilder.Entity<SurveyJob>()
               .Property(x => x.ModuleType)
               .HasConversion<string>();


            modelBuilder.Entity<SurveyJob>()
               .Property(x => x.Status)
               .HasConversion<string>();

            modelBuilder.Entity<SurveyAssignment>()
               .Property(x => x.Status)
               .HasConversion<string>();

            modelBuilder.Entity<SurveyJob>()
               .HasMany(x => x.Assignments)
               .WithOne(x => x.SurveyJob)
               .HasForeignKey(x => x.SurveyJobId)
               .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SurveyJob>()
               .HasMany(x => x.UploadedFiles)
               .WithOne(x => x.SurveyJob)
               .HasForeignKey(x => x.SurveyJobId)
               .OnDelete(DeleteBehavior.Cascade);


            modelBuilder.Entity<ExChecklistAnswer>()
               .Property(x => x.Result)
               .HasConversion<string>();

            modelBuilder.Entity<ExDefect>()
               .Property(x => x.Status)
               .HasConversion<string>();

            modelBuilder.Entity<ExEquipment>()
               .HasOne(x => x.SurveyJob)
               .WithMany()
               .HasForeignKey(x => x.SurveyJobId)
               .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ExChecklistAnswer>()
               .HasOne(x => x.SurveyJob)
               .WithMany()
               .HasForeignKey(x => x.SurveyJobId)
               .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ExChecklistAnswer>()
               .HasOne(x => x.ExEquipment)
               .WithMany()
               .HasForeignKey(x => x.ExEquipmentId)
               .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<ExChecklistAnswer>()
               .HasOne(x => x.ExChecklistQuestion)
               .WithMany()
               .HasForeignKey(x => x.ExChecklistQuestionId)
               .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<ExDefect>()
               .HasOne(x => x.SurveyJob)
               .WithMany()
               .HasForeignKey(x => x.SurveyJobId)
               .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ExDefect>()
               .HasOne(x => x.ExEquipment)
               .WithMany()
               .HasForeignKey(x => x.ExEquipmentId)
               .OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<IhmCheckpoint>()
               .Property(x => x.Result)
               .HasConversion<string>();

            modelBuilder.Entity<IhmArea>()
               .HasOne(x => x.SurveyJob)
               .WithMany()
               .HasForeignKey(x => x.SurveyJobId)
               .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<IhmArea>()
               .HasMany(x => x.Checkpoints)
               .WithOne(x => x.IhmArea)
               .HasForeignKey(x => x.IhmAreaId)
               .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<IhmCheckpoint>()
               .HasOne(x => x.SurveyJob)
               .WithMany()
               .HasForeignKey(x => x.SurveyJobId)
               .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<IhmCheckpointSubmission>()
               .Property(x => x.Status)
               .HasConversion<string>();

            modelBuilder.Entity<IhmCheckpointSubmission>()
               .HasIndex(x => new { x.IhmCheckpointId, x.SurveyorUserId })
               .IsUnique();

            modelBuilder.Entity<IhmCheckpointSubmission>()
               .HasOne(x => x.IhmCheckpoint)
               .WithMany(x => x.Submissions)
               .HasForeignKey(x => x.IhmCheckpointId)
               .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserNotification>()
               .HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });

            modelBuilder.Entity<IhmCheckpointSubmission>()
               .HasOne(x => x.SurveyJob)
               .WithMany()
               .HasForeignKey(x => x.SurveyJobId)
               .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<IhmImageMarker>()
               .HasOne(x => x.SurveyJob)
               .WithMany()
               .HasForeignKey(x => x.SurveyJobId)
               .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<IhmImageMarker>()
               .HasOne(x => x.IhmArea)
               .WithMany()
               .HasForeignKey(x => x.IhmAreaId)
               .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<IhmImageMarker>()
               .HasOne(x => x.IhmCheckpoint)
               .WithMany()
               .HasForeignKey(x => x.IhmCheckpointId)
               .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<IhmSamplingPhoto>()
               .HasOne(x => x.SurveyJob)
               .WithMany()
               .HasForeignKey(x => x.SurveyJobId)
               .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<IhmSamplingPhoto>()
               .HasOne(x => x.IhmArea)
               .WithMany()
               .HasForeignKey(x => x.IhmAreaId)
               .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<IhmSamplingPhoto>()
               .HasOne(x => x.IhmCheckpoint)
               .WithMany()
               .HasForeignKey(x => x.IhmCheckpointId)
               .OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<IhmImageMarker>()
                .Property(x => x.XPercent)
                .HasPrecision(5, 2);

            modelBuilder.Entity<IhmImageMarker>()
                .Property(x => x.YPercent)
                .HasPrecision(5, 2);
            modelBuilder.Entity<Vessel>()
               .Property(x => x.Status)
               .HasConversion<string>();

            modelBuilder.Entity<Vessel>()
               .HasIndex(x => x.ImoNumber);

            modelBuilder.Entity<SurveyJob>()
               .HasOne(x => x.Vessel)
               .WithMany(x => x.SurveyJobs)
               .HasForeignKey(x => x.VesselId)
               .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<IhmReportInfo>()
               .HasOne(x => x.SurveyJob)
               .WithOne(x => x.IhmReportInfo)
               .HasForeignKey<IhmReportInfo>(x => x.SurveyJobId)
               .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<IhmReportRevision>()
               .HasOne(x => x.SurveyJob)
               .WithMany()
               .HasForeignKey(x => x.SurveyJobId)
               .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<IhmAreaLayoutPlacement>()
                .HasOne(x => x.SurveyJob)
                .WithMany()
                .HasForeignKey(x => x.SurveyJobId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<IhmAreaLayoutPlacement>()
                .HasOne(x => x.IhmArea)
                .WithMany()
                .HasForeignKey(x => x.IhmAreaId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<IhmAreaLayoutPlacement>(entity =>
            {
                entity.Property(x => x.XPercent).HasPrecision(9, 4);
                entity.Property(x => x.YPercent).HasPrecision(9, 4);
                entity.Property(x => x.WidthPercent).HasPrecision(9, 4);
                entity.Property(x => x.HeightPercent).HasPrecision(9, 4);
                entity.Property(x => x.MatchConfidence).HasPrecision(9, 4);
            });

            modelBuilder.Entity<Project>()
    .HasIndex(x => x.Code)
    .IsUnique();

            modelBuilder.Entity<ProjectRole>()
                .HasIndex(x => new { x.ProjectId, x.RoleName })
                .IsUnique();

            modelBuilder.Entity<ClientVesselMapping>()
                .HasIndex(x => new { x.UserId, x.VesselId })
                .IsUnique();
            modelBuilder.Entity<ReportAssignment>()
     .HasIndex(x => new { x.ReportType, x.ReportId, x.ClientUserId, x.IsActive });

            modelBuilder.Entity<ReportAssignment>()
                 .HasIndex(x => new { x.ReportType, x.ReportId, x.ClientUserId })
                 .IsUnique()
                 .HasFilter("[IsActive] = 1");

            modelBuilder.Entity<ReportAssignment>()
                 .HasOne(x => x.ClientUser)
                 .WithMany()
                 .HasForeignKey(x => x.ClientUserId)
                 .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<ReportAssignment>()
                 .HasOne(x => x.AssignedByUser)
                 .WithMany()
                 .HasForeignKey(x => x.AssignedByUserId)
                 .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<ReportAssignment>()
                 .HasOne(x => x.RevokedByUser)
                 .WithMany()
                 .HasForeignKey(x => x.RevokedByUserId)
                 .OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<Project>().HasData(
                new Project { Id = 1, Name = "IHM", Code = "IHM", IsActive = true },
                new Project { Id = 2, Name = "EX Survey", Code = "EX_SURVEY", IsActive = true },
                new Project { Id = 3, Name = "AURAS", Code = "AURAS", IsActive = true }
            );

            modelBuilder.Entity<ProjectRole>().HasData(
                new ProjectRole { Id = 1, ProjectId = 1, RoleName = "Manager", IsPublicRegistrationAllowed = true },
                new ProjectRole { Id = 2, ProjectId = 1, RoleName = "Surveyor", IsPublicRegistrationAllowed = true },
                //new ProjectRole { Id = 3, ProjectId = 1, RoleName = "User", IsPublicRegistrationAllowed = true },
                new ProjectRole { Id = 4, ProjectId = 1, RoleName = "Client", IsPublicRegistrationAllowed = true },

                new ProjectRole { Id = 5, ProjectId = 2, RoleName = "Manager", IsPublicRegistrationAllowed = true },
                new ProjectRole { Id = 6, ProjectId = 2, RoleName = "Surveyor", IsPublicRegistrationAllowed = true },
                //new ProjectRole { Id = 7, ProjectId = 2, RoleName = "User", IsPublicRegistrationAllowed = true },
                new ProjectRole { Id = 8, ProjectId = 2, RoleName = "Client", IsPublicRegistrationAllowed = true },

                new ProjectRole { Id = 9, ProjectId = 3, RoleName = "Manager", IsPublicRegistrationAllowed = true },
                new ProjectRole { Id = 10, ProjectId = 3, RoleName = "User", IsPublicRegistrationAllowed = true }
                //new ProjectRole { Id = 11, ProjectId = 3, RoleName = "Client", IsPublicRegistrationAllowed = false },

                //new ProjectRole { Id = 12, ProjectId = 3, RoleName = "Admin", IsPublicRegistrationAllowed = false }
            );

            base.OnModelCreating(modelBuilder);
        }
    }

}

