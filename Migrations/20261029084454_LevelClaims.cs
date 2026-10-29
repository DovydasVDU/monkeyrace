using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace monkeyrace.Migrations
{
    /// <inheritdoc />
    public partial class LevelClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClaimedLevel",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            // Esami žaidėjai: bananai už lygius iki jų SENO lygio jau išduoti (senos ribos: 100, 250, ... 3200).
            // Aukštesnius lygius (po išplėtimo iki 100) sistema sumoka per pirmą kitą lenktynių / pamokos / profilio užkrovimą.
            migrationBuilder.Sql(@"UPDATE Users SET ClaimedLevel = CASE
                WHEN Xp >= 3200 THEN 10 WHEN Xp >= 2500 THEN 9 WHEN Xp >= 1900 THEN 8
                WHEN Xp >= 1400 THEN 7 WHEN Xp >= 1000 THEN 6 WHEN Xp >= 700 THEN 5
                WHEN Xp >= 450 THEN 4 WHEN Xp >= 250 THEN 3 WHEN Xp >= 100 THEN 2 ELSE 1 END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimedLevel",
                table: "Users");
        }
    }
}
