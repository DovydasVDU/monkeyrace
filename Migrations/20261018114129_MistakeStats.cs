using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace monkeyrace.Migrations
{
    /// <inheritdoc />
    public partial class MistakeStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserKeyStats",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Expected = table.Column<string>(type: "TEXT", nullable: false),
                    Typed = table.Column<string>(type: "TEXT", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserKeyStats", x => new { x.UserId, x.Expected, x.Typed });
                    table.ForeignKey(
                        name: "FK_UserKeyStats_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserWordMistakes",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Word = table.Column<string>(type: "TEXT", nullable: false),
                    Misses = table.Column<int>(type: "INTEGER", nullable: false),
                    CorrectStreak = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserWordMistakes", x => new { x.UserId, x.Word });
                    table.ForeignKey(
                        name: "FK_UserWordMistakes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserKeyStats");

            migrationBuilder.DropTable(
                name: "UserWordMistakes");
        }
    }
}
