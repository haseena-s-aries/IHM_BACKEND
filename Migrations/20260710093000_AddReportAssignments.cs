using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Survey_Certification_App.Migrations
{
    public partial class AddReportAssignments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    SurveyJobId = table.Column<int>(type: "int", nullable: true),
                    ClientUserId = table.Column<int>(type: "int", nullable: false),
                    AssignedByUserId = table.Column<int>(type: "int", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportAssignments_users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReportAssignments_users_ClientUserId",
                        column: x => x.ClientUserId,
                        principalTable: "users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReportAssignments_users_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportAssignments_AssignedByUserId",
                table: "ReportAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAssignments_ClientUserId",
                table: "ReportAssignments",
                column: "ClientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAssignments_RevokedByUserId",
                table: "ReportAssignments",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAssignments_ReportType_ReportId_ClientUserId_IsActive",
                table: "ReportAssignments",
                columns: new[] { "ReportType", "ReportId", "ClientUserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportAssignments_ReportType_ReportId_ClientUserId",
                table: "ReportAssignments",
                columns: new[] { "ReportType", "ReportId", "ClientUserId" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.UpdateData(
                table: "ProjectRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "IsPublicRegistrationAllowed",
                value: true);

            migrationBuilder.UpdateData(
                table: "ProjectRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "IsPublicRegistrationAllowed",
                value: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ProjectRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "IsPublicRegistrationAllowed",
                value: false);

            migrationBuilder.UpdateData(
                table: "ProjectRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "IsPublicRegistrationAllowed",
                value: false);

            migrationBuilder.DropTable(
                name: "ReportAssignments");
        }
    }
}
