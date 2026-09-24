using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentSupersededById : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SupersededById",
                table: "Documents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_SupersededById",
                table: "Documents",
                column: "SupersededById");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Documents_SupersededById",
                table: "Documents",
                column: "SupersededById",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Documents_SupersededById",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_SupersededById",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "SupersededById",
                table: "Documents");
        }
    }
}
