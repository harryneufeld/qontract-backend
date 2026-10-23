using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationsAndNoticePeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MutedNotificationKinds",
                table: "Users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotificationDigest",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AutoRenews",
                table: "Contracts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "NoticePeriodUnit",
                table: "Contracts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NoticePeriodValue",
                table: "Contracts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RenewalMonths",
                table: "Contracts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: true),
                    ContractId = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Params = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DedupKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DismissedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SnoozedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ContractId",
                table: "Notifications",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_DedupKey",
                table: "Notifications",
                columns: new[] { "UserId", "DedupKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_TenantId_ResolvedAt",
                table: "Notifications",
                columns: new[] { "UserId", "TenantId", "ResolvedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropColumn(
                name: "MutedNotificationKinds",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotificationDigest",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AutoRenews",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "NoticePeriodUnit",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "NoticePeriodValue",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "RenewalMonths",
                table: "Contracts");
        }
    }
}
