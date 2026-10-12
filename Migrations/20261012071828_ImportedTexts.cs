using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace monkeyrace.Migrations
{
    /// <inheritdoc />
    public partial class ImportedTexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Author",
                table: "TextSnippets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Flags",
                table: "TextSnippets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "License",
                table: "TextSnippets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceId",
                table: "TextSnippets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "TextSnippets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Work",
                table: "TextSnippets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 16,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 17,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 18,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 19,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 20,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 21,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 22,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 23,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 24,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 25,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 26,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 27,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 28,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 29,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 30,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 31,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 32,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 33,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 34,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 35,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 36,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 37,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 38,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 39,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 40,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 70,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 71,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "TextSnippets",
                keyColumn: "Id",
                keyValue: 72,
                columns: new[] { "Author", "Flags", "License", "SourceId", "SourceUrl", "Work" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_TextSnippets_SourceId",
                table: "TextSnippets",
                column: "SourceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TextSnippets_SourceId",
                table: "TextSnippets");

            migrationBuilder.DropColumn(
                name: "Author",
                table: "TextSnippets");

            migrationBuilder.DropColumn(
                name: "Flags",
                table: "TextSnippets");

            migrationBuilder.DropColumn(
                name: "License",
                table: "TextSnippets");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "TextSnippets");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "TextSnippets");

            migrationBuilder.DropColumn(
                name: "Work",
                table: "TextSnippets");
        }
    }
}
