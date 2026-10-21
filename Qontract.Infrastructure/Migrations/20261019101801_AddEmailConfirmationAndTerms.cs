using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailConfirmationAndTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var sqlServer = ActiveProvider.Contains("SqlServer");
            var postgres = ActiveProvider.Contains("Npgsql");
            var guidType = sqlServer ? "uniqueidentifier" : postgres ? "uuid" : "TEXT";
            var intType = sqlServer ? "int" : postgres ? "integer" : "INTEGER";
            var dateType = sqlServer ? "datetime2" : postgres ? "timestamp with time zone" : "TEXT";
            string StringType(int length) => sqlServer ? $"nvarchar({length})" : postgres ? $"character varying({length})" : "TEXT";
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailConfirmedAt",
                table: "Users",
                type: dateType,
                nullable: true);

            // Accounts that already exist keep signing in when confirmation is switched on.
            migrationBuilder.Sql("""
                UPDATE "Users" SET "EmailConfirmedAt" = "CreatedAt";
                """);

            migrationBuilder.CreateTable(
                name: "EmailConfirmations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: guidType, nullable: false),
                    UserId = table.Column<int>(type: intType, nullable: false),
                    TokenHash = table.Column<string>(type: StringType(64), maxLength: 64, nullable: false),
                    NormalizedEmail = table.Column<string>(type: StringType(100), maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: dateType, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: dateType, nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: dateType, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailConfirmations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailConfirmations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TermsAcceptances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: guidType, nullable: false),
                    UserId = table.Column<int>(type: intType, nullable: false),
                    Version = table.Column<string>(type: StringType(50), maxLength: 50, nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: dateType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TermsAcceptances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TermsAcceptances_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailConfirmations_TokenHash",
                table: "EmailConfirmations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailConfirmations_UserId_CreatedAt",
                table: "EmailConfirmations",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TermsAcceptances_UserId_Version",
                table: "TermsAcceptances",
                columns: new[] { "UserId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailConfirmations");

            migrationBuilder.DropTable(
                name: "TermsAcceptances");

            migrationBuilder.DropColumn(
                name: "EmailConfirmedAt",
                table: "Users");
        }
    }
}
