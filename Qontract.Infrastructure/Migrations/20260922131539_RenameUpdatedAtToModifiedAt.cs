using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameUpdatedAtToModifiedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "Users",
                newName: "ModifiedAt");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "Tenants",
                newName: "ModifiedAt");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "Documents",
                newName: "ModifiedAt");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "Contracts",
                newName: "ModifiedAt");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "Contractors",
                newName: "ModifiedAt");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "Addresses",
                newName: "ModifiedAt");

            migrationBuilder.AlterColumn<string>(
                name: "ModifiedBy",
                table: "Documents",
                type: "TEXT",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                table: "Documents",
                type: "TEXT",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 500);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ModifiedAt",
                table: "Users",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "ModifiedAt",
                table: "Tenants",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "ModifiedAt",
                table: "Documents",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "ModifiedAt",
                table: "Contracts",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "ModifiedAt",
                table: "Contractors",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "ModifiedAt",
                table: "Addresses",
                newName: "UpdatedAt");

            migrationBuilder.AlterColumn<string>(
                name: "ModifiedBy",
                table: "Documents",
                type: "TEXT",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                table: "Documents",
                type: "TEXT",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
