using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var sqlServer = ActiveProvider.Contains("SqlServer");
            var postgres = ActiveProvider.Contains("Npgsql");
            var guidType = sqlServer ? "uniqueidentifier" : postgres ? "uuid" : "TEXT";
            var dateType = sqlServer ? "datetime2" : postgres ? "timestamp with time zone" : "TEXT";
            var boolType = sqlServer ? "bit" : postgres ? "boolean" : "INTEGER";
            migrationBuilder.AddColumn<bool>(
                name: "RequiresPasswordSetup",
                table: "Users",
                type: boolType,
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PasswordResets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: guidType, nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    TokenHash = table.Column<string>(type: sqlServer ? "nvarchar(64)" : postgres ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    NormalizedEmail = table.Column<string>(type: sqlServer ? "nvarchar(100)" : postgres ? "character varying(100)" : "TEXT", maxLength: 100, nullable: false),
                    AuthVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: dateType, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: dateType, nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: dateType, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasswordResets_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResets_TokenHash",
                table: "PasswordResets",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResets_UserId_CreatedAt",
                table: "PasswordResets",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasswordResets");

            migrationBuilder.DropColumn(
                name: "RequiresPasswordSetup",
                table: "Users");
        }
    }
}
