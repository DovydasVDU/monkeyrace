using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace monkeyrace.Migrations
{
    /// <inheritdoc />
    public partial class PlayerProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RaceResults_RaceId",
                table: "RaceResults");

            migrationBuilder.AddColumn<int>(
                name: "Bananas",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Xp",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BananasGained",
                table: "RaceResults",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "XpGained",
                table: "RaceResults",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_RaceResults_RaceId_UserId",
                table: "RaceResults",
                columns: new[] { "RaceId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RaceResults_RaceId_UserId",
                table: "RaceResults");

            migrationBuilder.DropColumn(
                name: "Bananas",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Xp",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BananasGained",
                table: "RaceResults");

            migrationBuilder.DropColumn(
                name: "XpGained",
                table: "RaceResults");

            migrationBuilder.CreateIndex(
                name: "IX_RaceResults_RaceId",
                table: "RaceResults",
                column: "RaceId");
        }
    }
}
