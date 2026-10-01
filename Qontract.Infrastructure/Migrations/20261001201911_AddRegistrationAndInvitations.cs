using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationAndInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                table: "Users",
                
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // Fail on duplicate normalized emails instead of silently merging
            // distinct accounts. Inspect legacy duplicates before upgrading.
            migrationBuilder.Sql("""
                UPDATE "Users" SET "NormalizedEmail" = UPPER(TRIM("Email"));
                """);

            migrationBuilder.CreateTable(
                name: "TenantInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>( nullable: false),
                    TenantId = table.Column<int>( nullable: false),
                    CodeHash = table.Column<string>( maxLength: 64, nullable: false),
                    Email = table.Column<string>( maxLength: 100, nullable: true),
                    Role = table.Column<int>( nullable: false),
                    ExpiresAt = table.Column<DateTime>( nullable: false),
                    RevokedAt = table.Column<DateTime>( nullable: true),
                    RedeemedAt = table.Column<DateTime>( nullable: true),
                    RedeemedByUserId = table.Column<int>( nullable: true),
                    CreatedAt = table.Column<DateTime>( nullable: false),
                    ModifiedAt = table.Column<DateTime>( nullable: false),
                    CreatedBy = table.Column<string>( maxLength: 500, nullable: true),
                    ModifiedBy = table.Column<string>( maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantInvitations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantInvitations_Users_RedeemedByUserId",
                        column: x => x.RedeemedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantInvitations_CodeHash",
                table: "TenantInvitations",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantInvitations_RedeemedByUserId",
                table: "TenantInvitations",
                column: "RedeemedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantInvitations_TenantId",
                table: "TenantInvitations",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantInvitations");

            migrationBuilder.DropIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                table: "Users");
        }
    }
}

