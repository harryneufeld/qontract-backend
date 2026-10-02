using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContractorLegacyMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Contractors",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Contractors",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "Contractors",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "Contractors");
        }
    }
}
