using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContractResponsibilityAndTrash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contracts_TenantId",
                table: "Contracts");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Contracts",
                type: ActiveProvider.Contains("SqlServer") ? "datetime2" : ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "Contracts",
                type: ActiveProvider.Contains("SqlServer") ? "nvarchar(500)" : ActiveProvider.Contains("Npgsql") ? "character varying(500)" : "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResponsibleUserId",
                table: "Contracts",
                type: ActiveProvider.Contains("SqlServer") ? "int" : ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetentionExpiresAt",
                table: "Contracts",
                type: ActiveProvider.Contains("SqlServer") ? "datetime2" : ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_ResponsibleUserId",
                table: "Contracts",
                column: "ResponsibleUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_TenantId_ResponsibleUserId_DeletedAt",
                table: "Contracts",
                columns: new[] { "TenantId", "ResponsibleUserId", "DeletedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Contracts_Users_ResponsibleUserId",
                table: "Contracts",
                column: "ResponsibleUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            // Old refactor deletions used IsActive=false. Keep them hidden with a
            // fresh grace period. SQLite import provenance distinguishes genuine
            // legacy inactivity without interpreting audit actors as owners.
            if (ActiveProvider.Contains("Sqlite"))
            {
                migrationBuilder.Sql("CREATE TABLE IF NOT EXISTS LocalSourceLedger (Entity TEXT NOT NULL, SourceId TEXT NOT NULL, TargetReference TEXT, RawJson TEXT NOT NULL, PRIMARY KEY(Entity, SourceId))");
                migrationBuilder.Sql("""
                    UPDATE Contracts SET DeletedAt=strftime('%Y-%m-%d %H:%M:%f','now'),
                        RetentionExpiresAt=strftime('%Y-%m-%d %H:%M:%f','now','+90 days'), DeletedBy=ModifiedBy
                    WHERE IsActive=0 AND NOT EXISTS (
                        SELECT 1 FROM LocalSourceLedger l WHERE l.Entity='Contracts'
                        AND l.TargetReference=CAST(Contracts.Id AS TEXT)
                        AND json_extract(l.RawJson,'$.IsActive')=0);
                    UPDATE Contracts SET ResponsibleUserId=OwnerUserId WHERE TenantId IS NULL;
                    UPDATE Contracts SET ResponsibleUserId=(
                        SELECT CAST(u.TargetReference AS INTEGER) FROM LocalSourceLedger c
                        JOIN LocalSourceLedger u ON u.Entity='Users' AND u.SourceId=json_extract(c.RawJson,'$.UserId')
                        JOIN Users owner ON owner.Id=CAST(u.TargetReference AS INTEGER) AND owner.IsActive=1
                        JOIN TenantMemberships m ON m.UserId=owner.Id AND m.TenantId=Contracts.TenantId AND m.IsActive=1
                        WHERE c.Entity='Contracts' AND c.TargetReference=CAST(Contracts.Id AS TEXT))
                    WHERE TenantId IS NOT NULL;
                    """);
            }
            else if (ActiveProvider.Contains("SqlServer"))
                migrationBuilder.Sql("UPDATE Contracts SET DeletedAt=SYSUTCDATETIME(), RetentionExpiresAt=DATEADD(day,90,SYSUTCDATETIME()), DeletedBy=ModifiedBy WHERE IsActive=0; UPDATE Contracts SET ResponsibleUserId=OwnerUserId WHERE TenantId IS NULL;");
            else if (ActiveProvider.Contains("Npgsql"))
                migrationBuilder.Sql("UPDATE \"Contracts\" SET \"DeletedAt\"=CURRENT_TIMESTAMP, \"RetentionExpiresAt\"=CURRENT_TIMESTAMP + INTERVAL '90 days', \"DeletedBy\"=\"ModifiedBy\" WHERE NOT \"IsActive\"; UPDATE \"Contracts\" SET \"ResponsibleUserId\"=\"OwnerUserId\" WHERE \"TenantId\" IS NULL;");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contracts_Users_ResponsibleUserId",
                table: "Contracts");

            migrationBuilder.DropIndex(
                name: "IX_Contracts_ResponsibleUserId",
                table: "Contracts");

            migrationBuilder.DropIndex(
                name: "IX_Contracts_TenantId_ResponsibleUserId_DeletedAt",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "ResponsibleUserId",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "RetentionExpiresAt",
                table: "Contracts");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_TenantId",
                table: "Contracts",
                column: "TenantId");
        }
    }
}
