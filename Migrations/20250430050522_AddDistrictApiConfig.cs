using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ORSV2.Migrations
{
    /// <inheritdoc />
    public partial class AddDistrictApiConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CDSCode",
                table: "Districts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Districts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SISApiKey",
                table: "Districts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SISApiSecret",
                table: "Districts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SISBaseUrl",
                table: "Districts",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CDSCode",
                table: "Districts");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Districts");

            migrationBuilder.DropColumn(
                name: "SISApiKey",
                table: "Districts");

            migrationBuilder.DropColumn(
                name: "SISApiSecret",
                table: "Districts");

            migrationBuilder.DropColumn(
                name: "SISBaseUrl",
                table: "Districts");
        }
    }
}
