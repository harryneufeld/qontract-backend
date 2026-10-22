using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationsAndNoticePeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var sqlServer = ActiveProvider.Contains("SqlServer");
            var postgres = ActiveProvider.Contains("Npgsql");
            var guidType = sqlServer ? "uniqueidentifier" : postgres ? "uuid" : "TEXT";
            var intType = sqlServer ? "int" : postgres ? "integer" : "INTEGER";
            var boolType = sqlServer ? "bit" : postgres ? "boolean" : "INTEGER";
            var dayType = sqlServer ? "date" : postgres ? "date" : "TEXT";
            var dateType = sqlServer ? "datetime2" : postgres ? "timestamp with time zone" : "TEXT";
            string StringType(int length) => sqlServer ? $"nvarchar({length})" : postgres ? $"character varying({length})" : "TEXT";
            migrationBuilder.AddColumn<string>(
                name: "MutedNotificationKinds",
                table: "Users",
                type: StringType(500),
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotificationDigest",
                table: "Users",
                type: intType,
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AutoRenews",
                table: "Contracts",
                type: boolType,
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "NoticePeriodUnit",
                table: "Contracts",
                type: intType,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NoticePeriodValue",
                table: "Contracts",
                type: intType,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RenewalMonths",
                table: "Contracts",
                type: intType,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: guidType, nullable: false),
                    UserId = table.Column<int>(type: intType, nullable: false),
                    TenantId = table.Column<int>(type: intType, nullable: true),
                    ContractId = table.Column<int>(type: intType, nullable: true),
                    Kind = table.Column<string>(type: StringType(50), maxLength: 50, nullable: false),
                    Severity = table.Column<int>(type: intType, nullable: false),
                    DueOn = table.Column<DateOnly>(type: dayType, nullable: true),
                    Params = table.Column<string>(type: StringType(2000), maxLength: 2000, nullable: false),
                    DedupKey = table.Column<string>(type: StringType(200), maxLength: 200, nullable: false),
                    ReadAt = table.Column<DateTime>(type: dateType, nullable: true),
                    DismissedAt = table.Column<DateTime>(type: dateType, nullable: true),
                    SnoozedUntil = table.Column<DateTime>(type: dateType, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: dateType, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: dateType, nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: dateType, nullable: false),
                    CreatedBy = table.Column<string>(type: StringType(500), maxLength: 500, nullable: true),
                    ModifiedBy = table.Column<string>(type: StringType(500), maxLength: 500, nullable: true)
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
