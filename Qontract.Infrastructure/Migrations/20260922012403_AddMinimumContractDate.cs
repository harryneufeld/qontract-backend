using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMinimumContractDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MinimumContractDate",
                table: "Contracts",
                type: "TEXT",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinimumContractDate",
                table: "Contracts");
        }
    }
}
