// Data/AppDbContext.cs
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;
using System.Collections.Generic;
public class AppDbContext : DbContext
{
    // šioje klasėje aprašomos lentelės, esančios duomenų bazėje. Kiekvienas DbSet yra viena lentelė duomenų bazėje.
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<User> Users { get; set; }
    public DbSet<Race> Races { get; set; }
    public DbSet<RaceResult> RaceResults { get; set; }
    public DbSet<TextSnippet> TextSnippets { get; set; }
    public DbSet<UserKeyStat> UserKeyStats { get; set; }
    public DbSet<UserWordMistake> UserWordMistakes { get; set; }
    public DbSet<UserItem> UserItems { get; set; }
    public DbSet<UserLessonProgress> UserLessonProgresses { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Unikalus slapyvardis DB lygiu: AnyAsync patikra Register metode nepadeda, jei du registruojasi vienu metu
        modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
        // Vienas rezultatas vienam žaidėjui vienose lenktynėse: apsauga nuo dvigubo apdovanojimo
        modelBuilder.Entity<RaceResult>().HasIndex(r => new { r.RaceId, r.UserId }).IsUnique();
        // Importuotas tekstas DB tik vieną kartą
        modelBuilder.Entity<TextSnippet>().HasIndex(t => t.SourceId).IsUnique();
        // Klaidų statistika: viena eilutė vienai porai / vienam žodžiui (RaceService.SaveMistakesAsync)
        modelBuilder.Entity<UserKeyStat>().HasKey(k => new { k.UserId, k.Expected, k.Typed });
        modelBuilder.Entity<UserWordMistake>().HasKey(w => new { w.UserId, w.Word });
        // Ta pati prekė perkama tik kartą (RaceService.EquipItemAsync)
        modelBuilder.Entity<UserItem>().HasKey(i => new { i.UserId, i.ItemId });
        // Pamokų progresas: viena eilutė žaidėjui ir pamokai (RaceService.SaveLessonResultAsync)
        modelBuilder.Entity<UserLessonProgress>().HasKey(p => new { p.UserId, p.LessonId });

        // Seed data – pradiniai duomenys. Tekstus padarome iškart, kad nereiktų jų suvedinėti ranka
        modelBuilder.Entity<TextSnippet>().HasData(
            new TextSnippet { Id = 1, Content = "Greitas ruda lapė šokinėja per tingų šunį", WordCount = 8 },
            new TextSnippet { Id = 2, Content = "Programavimas yra menas spręsti problemas elegantiškai", WordCount = 7 },
            new TextSnippet { Id = 3, Content = "Saulė kyla kiekvieną rytą ir primena apie naują pradžią. Kiekviena diena gali būti geresnė už vakarykštę.", WordCount = 17 },
            new TextSnippet { Id = 4, Content = "Maži žingsniai veda į didelius tikslus. Svarbiausia yra nesustoti. Kantrybė visada atsiperka.", WordCount = 15 },
            new TextSnippet { Id = 5, Content = "Lietus plauna gatves ir mintis. Po audros visada ateina ramybė.", WordCount = 12 },
            new TextSnippet { Id = 6, Content = "Drąsa prasideda nuo sprendimo pabandyti. Net jei bijai, judėk pirmyn.", WordCount = 13 },
            new TextSnippet { Id = 7, Content = "Knygos atveria pasaulius. Skaitydamas mokaisi suprasti kitus. Žodžiai turi galią.", WordCount = 12 },
            new TextSnippet { Id = 8, Content = "Draugystė gimsta iš paprastų pokalbių. Tikras ryšys stiprėja laikui bėgant.", WordCount = 12 },
            new TextSnippet { Id = 9, Content = "Ryto kava sušildo rankas ir mintis. Tyla kartais pasako daugiau nei žodžiai.", WordCount = 14 },
            new TextSnippet { Id = 10, Content = "Vaikystės svajonės niekur nedingsta. Jos laukia, kol vėl patikėsi savimi.", WordCount = 13 },
            new TextSnippet { Id = 11, Content = "Kiekvienas medis prasidėjo nuo mažos sėklos. Augimas reikalauja laiko.", WordCount = 11 },
            new TextSnippet { Id = 12, Content = "Šypsena gali pakeisti dieną. Geras žodis nieko nekainuoja.", WordCount = 9 },
            new TextSnippet { Id = 13, Content = "Mokykis iš klaidų ir judėk toliau. Tobulumas ateina su praktika.", WordCount = 12 },
            new TextSnippet { Id = 14, Content = "Kelionė prasideda nuo pirmo žingsnio. Svarbu išdrįsti pradėti.", WordCount = 10 },
            new TextSnippet { Id = 15, Content = "Laikas gydo žaizdas. Kantrybė stiprina širdį.", WordCount = 7 },
            new TextSnippet { Id = 16, Content = "Svajoji tyliai, bet veiki drąsiai. Taip gimsta pokyčiai.", WordCount = 9 },
            new TextSnippet { Id = 17, Content = "Vėjas keičia kryptį, bet tikslas lieka tas pats. Nepamiršk savo kelio.", WordCount = 14 },
            new TextSnippet { Id = 18, Content = "Gamta moko paprastumo. Upė teka be skubėjimo. Ji vis tiek pasiekia jūrą.", WordCount = 14 },
            new TextSnippet { Id = 19, Content = "Kiekviena klaida yra pamoka. Svarbiausia iš jos pasimokyti.", WordCount = 10 },
            new TextSnippet { Id = 20, Content = "Svajonės auga su tikėjimu. Abejonės jas lėtina.", WordCount = 7 },
            new TextSnippet { Id = 21, Content = "Tyli naktis leidžia išgirsti save. Kartais atsakymai slypi paprastume.", WordCount = 11 },
            new TextSnippet { Id = 22, Content = "Darbas duoda vaisių. Pastangos niekada nedingsta be pėdsako.", WordCount = 9 },
            new TextSnippet { Id = 23, Content = "Klausyk širdies, bet nepamiršk proto. Pusiausvyra kuria harmoniją.", WordCount = 9 },
            new TextSnippet { Id = 24, Content = "Mažas gerumas keičia pasaulį. Viskas prasideda nuo tavęs.", WordCount = 9 },
            new TextSnippet { Id = 25, Content = "Ryžtas nugali baimę. Veiksmas gimdo pasitikėjimą.", WordCount = 6 },
            new TextSnippet { Id = 26, Content = "Sėkmė ateina tiems, kurie dirba tyliai. Rezultatai kalba garsiai.", WordCount = 11 },
            new TextSnippet { Id = 27, Content = "Kelias gali būti ilgas. Bet kiekvienas žingsnis artina prie tikslo.", WordCount = 13 },
            new TextSnippet { Id = 28, Content = "Viltis šviečia net tamsoje. Ji niekada neužgęsta visiškai.", WordCount = 9 },
            new TextSnippet { Id = 29, Content = "Tylus darbas kuria stiprų pagrindą. Skubėjimas dažnai klaidina.", WordCount = 10 },
            new TextSnippet { Id = 30, Content = "Kiekviena diena yra dovana. Priimk ją su dėkingumu.", WordCount = 10 },
            new TextSnippet { Id = 31, Content = "Stiprybė slypi paprastume. Tikėjimas savimi keičia viską.", WordCount = 8 },
            new TextSnippet { Id = 32, Content = "Svajonės neturi termino. Jos laukia tavo sprendimo.", WordCount = 8 },
            new TextSnippet { Id = 33, Content = "Ramybė prasideda viduje. Pasaulis tik atspindi tavo būseną.", WordCount = 9 },
            new TextSnippet { Id = 34, Content = "Nebijok pradėti iš naujo. Kartais tai geriausias sprendimas.", WordCount = 10 },
            new TextSnippet { Id = 35, Content = "Maži įpročiai kuria didelius pokyčius. Kartojimas stiprina charakterį.", WordCount = 9 },
            new TextSnippet { Id = 36, Content = "Tikras augimas prasideda už komforto ribų. Iššūkiai padeda pažinti save. Kiekvienas sunkumas slepia galimybę. Svarbu ją pastebėti.", WordCount = 18 },
            new TextSnippet { Id = 37, Content = "Kūryba gimsta iš drąsos suklysti. Idėjos dažnai atrodo trapios pradžioje. Tačiau kantrybė leidžia joms subręsti. Rezultatas nustebina net patį kūrėją.", WordCount = 21 },
            new TextSnippet { Id = 38, Content = "Žmogaus potencialas neretai lieka neišnaudotas. Baimė sukausto sprendimus. Vis dėlto pasiryžimas gali pakeisti kryptį. Vienas veiksmas kartais lemia viską.", WordCount = 20 },
            new TextSnippet { Id = 39, Content = "Technologijos palengvina kasdienybę, tačiau jos neatstoja žmogiško ryšio. Bendravimas ugdo empatiją. Tikras dialogas reikalauja dėmesio. Skubėjimas jį silpnina.", WordCount = 20 },
            new TextSnippet { Id = 40, Content = "Kritika gali būti skaudi, bet ji skatina tobulėti. Brandumas leidžia ją priimti ramiai. Atsakas turėtų būti apgalvotas. Emocijos neturi valdyti sprendimų.", WordCount = 20 },
            new TextSnippet { Id = 70, Content = "Gyvenimas nėra tiesi linija – jis primena sudėtingą grafiką su pakilimais ir nuosmukiais. Kartais atrodo, kad formulė per sudėtinga, tačiau sprendimas slypi detalėse. Disciplina × kantrybė = progresas. Net 1% pagerėjimas kasdien per metus duoda stulbinantį rezultatą. Klausimas tik vienas: ar pasiryši pradėti šiandien?", WordCount = 42 },
            new TextSnippet { Id = 71, Content = "Sėkmė retai ateina staiga; dažniausiai ji kaupiasi lėtai, beveik nepastebimai. 10 bandymų gali baigtis nesėkme, bet 11-asis pakeičia viską. Procesas reikalauja strategijos, analizės ir savirefleksijos. Kartais verta sustoti, peržiūrėti planą ir optimizuoti veiksmus. Rezultatas priklauso nuo nuoseklumo, o ne nuo sėkmės procento.", WordCount = 39 },
            new TextSnippet { Id = 72, Content = "Informacijos amžiuje dėmesys tapo valiuta. Kiek minučių per dieną skiri savo tikslams – 5 ar 50? Skirtumas tarp 0 ir 1 sprendimo atrodo mažas, bet pasekmės milžiniškos. Triukšmas blaško, o tyla leidžia susikaupti. Sąmoningumas šiandien yra supergalia.", WordCount = 36 }
        );
    }
}