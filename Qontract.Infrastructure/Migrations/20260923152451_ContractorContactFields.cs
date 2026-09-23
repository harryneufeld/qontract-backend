using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContractorContactFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactInfo",
                table: "Contractors");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Contractors",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Contractors",
                type: "TEXT",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Contractors");

            migrationBuilder.AddColumn<string>(
                name: "ContactInfo",
                table: "Contractors",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }
    }
}
