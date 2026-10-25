using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenSessions : Migration
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
                name: "ClientType",
                table: "AuthSessions",
                type: StringType(16),
                maxLength: 16,
                nullable: false,
                defaultValue: "api");

            migrationBuilder.AddColumn<string>(
                name: "DeviceName",
                table: "AuthSessions",
                type: StringType(100),
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IdleExpiresAt",
                table: "AuthSessions",
                type: dateType,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUsedAt",
                table: "AuthSessions",
                type: dateType,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousRefreshTokenHash",
                table: "AuthSessions",
                type: StringType(64),
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefreshTokenHash",
                table: "AuthSessions",
                type: StringType(64),
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RotatedAt",
                table: "AuthSessions",
                type: dateType,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AuthSessions",
                type: intType,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "AuthSessions",
                type: StringType(256),
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_PreviousRefreshTokenHash",
                table: "AuthSessions",
                column: "PreviousRefreshTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_RefreshTokenHash",
                table: "AuthSessions",
                column: "RefreshTokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuthSessions_PreviousRefreshTokenHash",
                table: "AuthSessions");

            migrationBuilder.DropIndex(
                name: "IX_AuthSessions_RefreshTokenHash",
                table: "AuthSessions");

            migrationBuilder.DropColumn(
                name: "ClientType",
                table: "AuthSessions");

            migrationBuilder.DropColumn(
                name: "DeviceName",
                table: "AuthSessions");

            migrationBuilder.DropColumn(
                name: "IdleExpiresAt",
                table: "AuthSessions");

            migrationBuilder.DropColumn(
                name: "LastUsedAt",
                table: "AuthSessions");

            migrationBuilder.DropColumn(
                name: "PreviousRefreshTokenHash",
                table: "AuthSessions");

            migrationBuilder.DropColumn(
                name: "RefreshTokenHash",
                table: "AuthSessions");

            migrationBuilder.DropColumn(
                name: "RotatedAt",
                table: "AuthSessions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuthSessions");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "AuthSessions");
        }
    }
}
