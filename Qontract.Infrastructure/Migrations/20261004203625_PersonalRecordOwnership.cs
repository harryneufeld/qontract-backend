using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PersonalRecordOwnership : Migration
    {
        /// <inheritdoc />
        private string IntegerType => ActiveProvider.Contains("SqlServer") ? "int" : ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contractors_Tenants_TenantId",
                table: "Contractors");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Tenants_TenantId",
                table: "Users");

            migrationBuilder.AlterColumn<int>(
                name: "TenantId",
                table: "Users",
                type: IntegerType,
                nullable: true,
                oldClrType: typeof(int),
                oldType: IntegerType);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserId",
                table: "Contracts",
                type: IntegerType,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TenantId",
                table: "Contractors",
                type: IntegerType,
                nullable: true,
                oldClrType: typeof(int),
                oldType: IntegerType);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserId",
                table: "Contractors",
                type: IntegerType,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_OwnerUserId_TenantId",
                table: "Contracts",
                columns: new[] { "OwnerUserId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_Contractors_OwnerUserId_TenantId",
                table: "Contractors",
                columns: new[] { "OwnerUserId", "TenantId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Contractors_Tenants_TenantId",
                table: "Contractors",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Contractors_Users_OwnerUserId",
                table: "Contractors",
                column: "OwnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Contracts_Users_OwnerUserId",
                table: "Contracts",
                column: "OwnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Tenants_TenantId",
                table: "Users",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Personal ownership cannot be safely downgraded. Restore a matching pre-upgrade database and document backup instead.");
        }
    }
}