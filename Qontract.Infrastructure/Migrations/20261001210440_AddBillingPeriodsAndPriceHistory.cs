using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingPeriodsAndPriceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BillingPeriod",
                table: "Contracts",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    ContractId = table.Column<int>(nullable: false),
                    Value = table.Column<string>(maxLength: 500, nullable: true),
                    Currency = table.Column<string>(maxLength: 50, nullable: true),
                    BillingPeriod = table.Column<int>(nullable: true),
                    EffectiveFrom = table.Column<string>(maxLength: 10, nullable: false),
                    EffectiveUntil = table.Column<string>(maxLength: 10, nullable: true),
                    Notes = table.Column<string>(maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    ModifiedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(maxLength: 500, nullable: true),
                    ModifiedBy = table.Column<string>(maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractPrices_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractPrices_ContractId_EffectiveFrom",
                table: "ContractPrices",
                columns: new[] { "ContractId", "EffectiveFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractPrices");

            migrationBuilder.DropColumn(
                name: "BillingPeriod",
                table: "Contracts");
        }
    }
}
