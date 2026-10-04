using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qontract.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountAndTenantAddresses : Migration
    {
        /// <inheritdoc />
        private string TextType => ActiveProvider.Contains("SqlServer") ? "nvarchar(max)" : "TEXT";
        private string IntegerType => ActiveProvider.Contains("SqlServer") ? "int" : ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Street",
                table: "Addresses",
                type: TextType,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 500);

            foreach (var field in new[] { ("City", 100), ("Zip", 20), ("Country", 100) })
                migrationBuilder.AlterColumn<string>(name: field.Item1, table: "Addresses",
                    type: TextType, nullable: true, oldClrType: typeof(string), oldType: "TEXT",
                    oldMaxLength: field.Item2, oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HouseNumber",
                table: "Addresses",
                type: TextType,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Addresses",
                type: IntegerType,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Addresses_UserId",
                table: "Addresses",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Addresses_Users_UserId",
                table: "Addresses",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Account addresses and house numbers cannot be safely downgraded. Restore a matched pre-upgrade database backup instead.");
        }
    }
}