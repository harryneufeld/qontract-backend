using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTwoFactorAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var sqlServer = ActiveProvider.Contains("SqlServer");
            var postgres = ActiveProvider.Contains("Npgsql");
            var textType = sqlServer ? "nvarchar(max)" : postgres ? "text" : "TEXT";
            var boolType = sqlServer ? "bit" : postgres ? "boolean" : "INTEGER";
            var intType = sqlServer ? "int" : postgres ? "integer" : "INTEGER";
            var longType = sqlServer || postgres ? "bigint" : "INTEGER";
            var dateType = sqlServer ? "datetime2" : postgres ? "timestamp with time zone" : "TEXT";
            migrationBuilder.AddColumn<string>(
                name: "PendingTwoFactorSecret",
                table: "Users",
                type: textType,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TwoFactorEnabled",
                table: "Users",
                type: boolType,
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TwoFactorFailures",
                table: "Users",
                type: intType,
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "TwoFactorLastStep",
                table: "Users",
                type: longType,
                nullable: false,
                defaultValue: -1L);

            migrationBuilder.AddColumn<DateTime>(
                name: "TwoFactorLockedUntil",
                table: "Users",
                type: dateType,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorRecoveryHashes",
                table: "Users",
                type: textType,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorSecret",
                table: "Users",
                type: textType,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TwoFactorSetupExpiresAt",
                table: "Users",
                type: dateType,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingTwoFactorSecret",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TwoFactorEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TwoFactorFailures",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TwoFactorLastStep",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TwoFactorLockedUntil",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TwoFactorRecoveryHashes",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TwoFactorSecret",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TwoFactorSetupExpiresAt",
                table: "Users");
        }
    }
}
