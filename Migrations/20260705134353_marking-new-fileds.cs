using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Survey_Certification_App.Migrations
{
    /// <inheritdoc />
    public partial class markingnewfileds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManualForExistingImage",
                table: "IhmImageMarkers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "IhmImageMarkers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SuppressRender",
                table: "IhmImageMarkers",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsManualForExistingImage",
                table: "IhmImageMarkers");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "IhmImageMarkers");

            migrationBuilder.DropColumn(
                name: "SuppressRender",
                table: "IhmImageMarkers");
        }
    }
}
