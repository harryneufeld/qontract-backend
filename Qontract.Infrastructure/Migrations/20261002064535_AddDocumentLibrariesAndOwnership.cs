using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentLibrariesAndOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Contracts_ContractId",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Users_UserId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_UserId",
                table: "Documents");

            migrationBuilder.AlterColumn<int>(
                name: "ContractId",
                table: "Documents",

                nullable: true,
                oldClrType: typeof(int));

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Documents",

                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Documents",

                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Scope",
                table: "Documents",

                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Documents",

                nullable: true);

            // Preserve unresolved ownership. Family/contract tenancy can be
            // carried forward only when the existing association establishes it.
            migrationBuilder.Sql("""
                UPDATE "Documents" SET "TenantId" =
                    (SELECT "TenantId" FROM "Contracts" WHERE "Contracts"."Id" = "Documents"."ContractId")
                WHERE "TenantId" IS NULL;
                """);
            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId_Scope",
                table: "Documents",
                columns: new[] { "TenantId", "Scope" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UserId_Scope",
                table: "Documents",
                columns: new[] { "UserId", "Scope" });

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Contracts_ContractId",
                table: "Documents",
                column: "ContractId",
                principalTable: "Contracts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Tenants_TenantId",
                table: "Documents",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Users_UserId",
                table: "Documents",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Document libraries cannot be downgraded safely to required contract ownership. Restore a verified backup instead.");
        }
    }
}
