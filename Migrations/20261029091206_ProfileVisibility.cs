using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace monkeyrace.Migrations
{
    /// <inheritdoc />
    public partial class ProfileVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProfileVisibility",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfileVisibility",
                table: "Users");
        }
    }
}
