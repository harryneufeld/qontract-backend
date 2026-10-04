using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRevocableAuthSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The design-time snapshot is SQLite; native types must be selected
            // explicitly when generating the shared migration for other providers.
            var guidType = ActiveProvider.Contains("SqlServer") ? "uniqueidentifier"
                : ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT";
            var dateType = ActiveProvider.Contains("SqlServer") ? "datetime2"
                : ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT";
            migrationBuilder.AddColumn<int>(
                name: "AuthVersion",
                table: "Users",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AuthSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: guidType, nullable: false),
                    UserId = table.Column<int>(nullable: false),
                    AuthVersion = table.Column<int>(nullable: false),
                    CreatedAt = table.Column<DateTime>(type: dateType, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: dateType, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: dateType, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_UserId_ExpiresAt",
                table: "AuthSessions",
                columns: new[] { "UserId", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthSessions");

            migrationBuilder.DropColumn(
                name: "AuthVersion",
                table: "Users");
        }
    }
}
