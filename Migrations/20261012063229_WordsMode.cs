using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace monkeyrace.Migrations
{
    /// <inheritdoc />
    public partial class WordsMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Races_TextSnippets_TextSnippetId",
                table: "Races");

            migrationBuilder.AlterColumn<int>(
                name: "TextSnippetId",
                table: "Races",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "DurationSeconds",
                table: "Races",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Races_TextSnippets_TextSnippetId",
                table: "Races",
                column: "TextSnippetId",
                principalTable: "TextSnippets",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Races_TextSnippets_TextSnippetId",
                table: "Races");

            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "Races");

            migrationBuilder.AlterColumn<int>(
                name: "TextSnippetId",
                table: "Races",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Races_TextSnippets_TextSnippetId",
                table: "Races",
                column: "TextSnippetId",
                principalTable: "TextSnippets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
