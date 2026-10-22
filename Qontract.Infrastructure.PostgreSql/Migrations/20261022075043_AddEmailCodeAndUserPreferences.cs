using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailCodeAndUserPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultCurrency",
                table: "Users",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Users",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OnboardingCompletedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CodeAttempts",
                table: "EmailConfirmations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CodeExpiresAt",
                table: "EmailConfirmations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodeHash",
                table: "EmailConfirmations",
                type: "character varying(64)",
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
