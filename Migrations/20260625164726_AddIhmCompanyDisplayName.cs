using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Survey_Certification_App.Migrations
{
    /// <inheritdoc />
    public partial class AddIhmCompanyDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "IhmCompanies",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IhmCompanies_CompanyName_DisplayName_Email",
                table: "IhmCompanies",
                columns: new[] { "CompanyName", "DisplayName", "Email" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IhmCompanies_CompanyName_DisplayName_Email",
                table: "IhmCompanies");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "IhmCompanies");
        }
    }
}
