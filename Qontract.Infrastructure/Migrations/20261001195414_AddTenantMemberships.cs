using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantMemberships",
                columns: table => new
                {
                    UserId = table.Column<int>(nullable: false),
                    TenantId = table.Column<int>(nullable: false),
                    Role = table.Column<int>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    ModifiedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(maxLength: 500, nullable: true),
                    ModifiedBy = table.Column<string>(maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantMemberships", x => new { x.UserId, x.TenantId });
                    table.ForeignKey(
                        name: "FK_TenantMemberships_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Preserve each existing account's primary membership and role.
            // Selecting provider-native values keeps the backfill portable.
            migrationBuilder.Sql("""
                INSERT INTO "TenantMemberships"
                    ("UserId", "TenantId", "Role", "IsActive", "CreatedAt", "ModifiedAt", "CreatedBy", "ModifiedBy")
                SELECT "Id", "TenantId", "Role", "IsActive", "CreatedAt", "ModifiedAt", "CreatedBy", "ModifiedBy"
                FROM "Users";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_TenantMemberships_TenantId",
                table: "TenantMemberships",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantMemberships");
        }
    }
}

