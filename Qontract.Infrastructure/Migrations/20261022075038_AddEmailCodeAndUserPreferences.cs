using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailCodeAndUserPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var sqlServer = ActiveProvider.Contains("SqlServer");
            var postgres = ActiveProvider.Contains("Npgsql");
            var intType = sqlServer ? "int" : postgres ? "integer" : "INTEGER";
            var dateType = sqlServer ? "datetime2" : postgres ? "timestamp with time zone" : "TEXT";
            string StringType(int length) => sqlServer ? $"nvarchar({length})" : postgres ? $"character varying({length})" : "TEXT";
            migrationBuilder.AddColumn<string>(
                name: "DefaultCurrency",
                table: "Users",
                type: StringType(3),
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Users",
                type: StringType(5),
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OnboardingCompletedAt",
                table: "Users",
                type: dateType,
                nullable: true);

            // Accounts that exist before first-run setup was introduced must not be sent through it.
            migrationBuilder.Sql("UPDATE \"Users\" SET \"OnboardingCompletedAt\" = CURRENT_TIMESTAMP WHERE \"OnboardingCompletedAt\" IS NULL;");

            migrationBuilder.AddColumn<int>(
                name: "CodeAttempts",
                table: "EmailConfirmations",
                type: intType,
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CodeExpiresAt",
                table: "EmailConfirmations",
                type: dateType,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodeHash",
                table: "EmailConfirmations",
                type: StringType(64),
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultCurrency",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OnboardingCompletedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CodeAttempts",
                table: "EmailConfirmations");

            migrationBuilder.DropColumn(
                name: "CodeExpiresAt",
                table: "EmailConfirmations");

            migrationBuilder.DropColumn(
                name: "CodeHash",
                table: "EmailConfirmations");
        }
    }
}
