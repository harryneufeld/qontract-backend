using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTitleAndDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DocumentDate",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Documents",
                type: "TEXT",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentDate",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Documents");
        }
    }
}
