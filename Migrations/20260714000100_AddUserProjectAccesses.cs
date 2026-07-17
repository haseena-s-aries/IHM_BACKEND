using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Survey_Certification_App.Migrations
{
    public partial class AddUserProjectAccesses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserProjectAccesses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: true),
                    RoleName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    IsRejected = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProjectAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserProjectAccesses_ProjectRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "ProjectRoles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserProjectAccesses_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserProjectAccesses_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserProjectAccesses_ProjectId",
                table: "UserProjectAccesses",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProjectAccesses_RoleId",
                table: "UserProjectAccesses",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProjectAccesses_UserId_ProjectId",
                table: "UserProjectAccesses",
                columns: new[] { "UserId", "ProjectId" },
                unique: true);

            migrationBuilder.Sql(@"
INSERT INTO UserProjectAccesses
    (UserId, ProjectId, RoleId, RoleName, IsActive, IsApproved, IsRejected, CreatedAtUtc, UpdatedAtUtc)
SELECT
    u.Id,
    u.ProjectId,
    u.RoleId,
    COALESCE(NULLIF(u.RoleName, ''), u.Role),
    CAST(1 AS bit),
    u.IsApproved,
    u.IsRejected,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
FROM users u
WHERE u.ProjectId IS NOT NULL
  AND NOT EXISTS (
      SELECT 1
      FROM UserProjectAccesses a
      WHERE a.UserId = u.Id
        AND a.ProjectId = u.ProjectId
  );
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserProjectAccesses");
        }
    }
}
