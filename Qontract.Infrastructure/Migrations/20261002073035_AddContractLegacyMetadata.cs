using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContractLegacyMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Iban",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Contracts",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Iban",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Contracts");
        }
    }
}
