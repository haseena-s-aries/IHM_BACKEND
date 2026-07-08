using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Survey_Certification_App.Migrations
{
    public partial class AddSurveyJobAssignmentScope : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignedManagerId",
                table: "SurveyJobs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssignedUserId",
                table: "SurveyJobs",
                type: "int",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedManagerId",
                table: "SurveyJobs");

            migrationBuilder.DropColumn(
                name: "AssignedUserId",
                table: "SurveyJobs");
        }
    }
}
