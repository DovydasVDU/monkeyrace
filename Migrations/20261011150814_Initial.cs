using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace monkeyrace.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TextSnippets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    Language = table.Column<string>(type: "TEXT", nullable: true),
                    WordCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TextSnippets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", nullable: true),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Races",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TextSnippetId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsMultiplayer = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Races", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Races_TextSnippets_TextSnippetId",
                        column: x => x.TextSnippetId,
                        principalTable: "TextSnippets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RaceResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    RaceId = table.Column<int>(type: "INTEGER", nullable: false),
                    WPM = table.Column<int>(type: "INTEGER", nullable: false),
                    Accuracy = table.Column<double>(type: "REAL", nullable: false),
                    TimeSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaceResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RaceResults_Races_RaceId",
                        column: x => x.RaceId,
                        principalTable: "Races",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RaceResults_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "TextSnippets",
                columns: new[] { "Id", "Content", "Language", "WordCount" },
                values: new object[,]
                {
                    { 1, "Greitas ruda lapė šokinėja per tingų šunį", "lt", 8 },
                    { 2, "Programavimas yra menas spręsti problemas elegantiškai", "lt", 7 },
                    { 3, "Saulė kyla kiekvieną rytą ir primena apie naują pradžią. Kiekviena diena gali būti geresnė už vakarykštę.", "lt", 17 },
                    { 4, "Maži žingsniai veda į didelius tikslus. Svarbiausia yra nesustoti. Kantrybė visada atsiperka.", "lt", 15 },
                    { 5, "Lietus plauna gatves ir mintis. Po audros visada ateina ramybė.", "lt", 12 },
                    { 6, "Drąsa prasideda nuo sprendimo pabandyti. Net jei bijai, judėk pirmyn.", "lt", 13 },
                    { 7, "Knygos atveria pasaulius. Skaitydamas mokaisi suprasti kitus. Žodžiai turi galią.", "lt", 12 },
                    { 8, "Draugystė gimsta iš paprastų pokalbių. Tikras ryšys stiprėja laikui bėgant.", "lt", 12 },
                    { 9, "Ryto kava sušildo rankas ir mintis. Tyla kartais pasako daugiau nei žodžiai.", "lt", 14 },
                    { 10, "Vaikystės svajonės niekur nedingsta. Jos laukia, kol vėl patikėsi savimi.", "lt", 13 },
                    { 11, "Kiekvienas medis prasidėjo nuo mažos sėklos. Augimas reikalauja laiko.", "lt", 11 },
                    { 12, "Šypsena gali pakeisti dieną. Geras žodis nieko nekainuoja.", "lt", 9 },
                    { 13, "Mokykis iš klaidų ir judėk toliau. Tobulumas ateina su praktika.", "lt", 12 },
                    { 14, "Kelionė prasideda nuo pirmo žingsnio. Svarbu išdrįsti pradėti.", "lt", 10 },
                    { 15, "Laikas gydo žaizdas. Kantrybė stiprina širdį.", "lt", 7 },
                    { 16, "Svajoji tyliai, bet veiki drąsiai. Taip gimsta pokyčiai.", "lt", 9 },
                    { 17, "Vėjas keičia kryptį, bet tikslas lieka tas pats. Nepamiršk savo kelio.", "lt", 14 },
                    { 18, "Gamta moko paprastumo. Upė teka be skubėjimo. Ji vis tiek pasiekia jūrą.", "lt", 14 },
                    { 19, "Kiekviena klaida yra pamoka. Svarbiausia iš jos pasimokyti.", "lt", 10 },
                    { 20, "Svajonės auga su tikėjimu. Abejonės jas lėtina.", "lt", 7 },
                    { 21, "Tyli naktis leidžia išgirsti save. Kartais atsakymai slypi paprastume.", "lt", 11 },
                    { 22, "Darbas duoda vaisių. Pastangos niekada nedingsta be pėdsako.", "lt", 9 },
                    { 23, "Klausyk širdies, bet nepamiršk proto. Pusiausvyra kuria harmoniją.", "lt", 9 },
                    { 24, "Mažas gerumas keičia pasaulį. Viskas prasideda nuo tavęs.", "lt", 9 },
                    { 25, "Ryžtas nugali baimę. Veiksmas gimdo pasitikėjimą.", "lt", 6 },
                    { 26, "Sėkmė ateina tiems, kurie dirba tyliai. Rezultatai kalba garsiai.", "lt", 11 },
                    { 27, "Kelias gali būti ilgas. Bet kiekvienas žingsnis artina prie tikslo.", "lt", 13 },
                    { 28, "Viltis šviečia net tamsoje. Ji niekada neužgęsta visiškai.", "lt", 9 },
                    { 29, "Tylus darbas kuria stiprų pagrindą. Skubėjimas dažnai klaidina.", "lt", 10 },
                    { 30, "Kiekviena diena yra dovana. Priimk ją su dėkingumu.", "lt", 10 },
                    { 31, "Stiprybė slypi paprastume. Tikėjimas savimi keičia viską.", "lt", 8 },
                    { 32, "Svajonės neturi termino. Jos laukia tavo sprendimo.", "lt", 8 },
                    { 33, "Ramybė prasideda viduje. Pasaulis tik atspindi tavo būseną.", "lt", 9 },
                    { 34, "Nebijok pradėti iš naujo. Kartais tai geriausias sprendimas.", "lt", 10 },
                    { 35, "Maži įpročiai kuria didelius pokyčius. Kartojimas stiprina charakterį.", "lt", 9 },
                    { 36, "Tikras augimas prasideda už komforto ribų. Iššūkiai padeda pažinti save. Kiekvienas sunkumas slepia galimybę. Svarbu ją pastebėti.", "lt", 18 },
                    { 37, "Kūryba gimsta iš drąsos suklysti. Idėjos dažnai atrodo trapios pradžioje. Tačiau kantrybė leidžia joms subręsti. Rezultatas nustebina net patį kūrėją.", "lt", 21 },
                    { 38, "Žmogaus potencialas neretai lieka neišnaudotas. Baimė sukausto sprendimus. Vis dėlto pasiryžimas gali pakeisti kryptį. Vienas veiksmas kartais lemia viską.", "lt", 20 },
                    { 39, "Technologijos palengvina kasdienybę, tačiau jos neatstoja žmogiško ryšio. Bendravimas ugdo empatiją. Tikras dialogas reikalauja dėmesio. Skubėjimas jį silpnina.", "lt", 20 },
                    { 40, "Kritika gali būti skaudi, bet ji skatina tobulėti. Brandumas leidžia ją priimti ramiai. Atsakas turėtų būti apgalvotas. Emocijos neturi valdyti sprendimų.", "lt", 20 },
                    { 70, "Gyvenimas nėra tiesi linija – jis primena sudėtingą grafiką su pakilimais ir nuosmukiais. Kartais atrodo, kad formulė per sudėtinga, tačiau sprendimas slypi detalėse. Disciplina × kantrybė = progresas. Net 1% pagerėjimas kasdien per metus duoda stulbinantį rezultatą. Klausimas tik vienas: ar pasiryši pradėti šiandien?", "lt", 42 },
                    { 71, "Sėkmė retai ateina staiga; dažniausiai ji kaupiasi lėtai, beveik nepastebimai. 10 bandymų gali baigtis nesėkme, bet 11-asis pakeičia viską. Procesas reikalauja strategijos, analizės ir savirefleksijos. Kartais verta sustoti, peržiūrėti planą ir optimizuoti veiksmus. Rezultatas priklauso nuo nuoseklumo, o ne nuo sėkmės procento.", "lt", 39 },
                    { 72, "Informacijos amžiuje dėmesys tapo valiuta. Kiek minučių per dieną skiri savo tikslams – 5 ar 50? Skirtumas tarp 0 ir 1 sprendimo atrodo mažas, bet pasekmės milžiniškos. Triukšmas blaško, o tyla leidžia susikaupti. Sąmoningumas šiandien yra supergalia.", "lt", 36 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RaceResults_RaceId",
                table: "RaceResults",
                column: "RaceId");

            migrationBuilder.CreateIndex(
                name: "IX_RaceResults_UserId",
                table: "RaceResults",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Races_TextSnippetId",
                table: "Races",
                column: "TextSnippetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RaceResults");

            migrationBuilder.DropTable(
                name: "Races");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "TextSnippets");
        }
    }
}
