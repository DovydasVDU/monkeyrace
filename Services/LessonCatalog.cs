// Services/LessonCatalog.cs
using System;
using System.Collections.Generic;
using System.Linq;

public enum LessonKind
{
    // pačios pirmos pamokos: raidžių grupės, nes su 2–4 raidėmis tikrų žodžių nėra
    Drill,
    // tikri lietuviški žodžiai (jei žodžių per mažai, variklis priduria raidžių grupių)
    Words,
    // sakiniai iš tekstų (LessonEngine.SentenceItems)
    Sentences
}

public sealed class LessonModule
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Icon { get; init; } = "keyboard";
    public IReadOnlyList<Lesson> Lessons { get; init; } = Array.Empty<Lesson>();
}

public sealed class Lesson
{
    public string Id { get; init; } = "";
    // globali tvarka nuo 0 (svečiams nemokamos tik pirmos LessonCatalog.FreeLessons)
    public int Index { get; init; }
    public string ModuleId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Intro { get; init; } = "";
    // šioje pamokoje išmokstami simboliai; LessonCatalog.Shift = didžiosios raidės (Shift)
    public string NewKeys { get; init; } = "";
    // visi simboliai, leidžiami šioje pamokoje (ankstesnių pamokų NewKeys + šios)
    public string AllowedKeys { get; init; } = "";
    public LessonKind Kind { get; init; }
    // 2 = klaviatūra ir rankos, 1 = tik klaviatūra, 0 = be pagalbos (žaidėjas gali pakeisti pats)
    public int Hints { get; init; }
    // pradinis žodžių sunkumas (0–4), LessonEngine toliau keičia pagal sekimąsi
    public int StartLevel { get; init; } = 1;
    // 0 – nėra; kitaip 3 žvaigždutės tik pasiekus šį WPM
    public int TargetWpm { get; init; }
    // didžiųjų raidžių pamoka: žodžiai rašomi pradedant didžiąja
    public bool Capitals { get; init; }

    public bool IsReview => NewKeys.Length == 0;
    // pamoka, kurioje mokomasi rašyti didžiąsias lietuviškas raides (Ą, Č, Š, Ž ...)
    public bool LtCapitals => NewKeys.Contains(LessonCatalog.LtCapitals);
}

// Visa mokymo programa vienoje vietoje (kaip ShopCatalog). Pamokos eina iš eilės; raidės kaupiamos:
// kiekviena pamoka leidžia visas ankstesnių pamokų raides, todėl žodžiai visada sudaryti tik iš išmoktų klavišų.
public static class LessonCatalog
{
    // tiek pirmųjų pamokų galima atlikti neprisijungus (progresas naršyklėje), toliau reikia paskyros
    public const int FreeLessons = 3;
    // „Shift“ kaip mokomas klavišas (didžiosios raidės)
    public const char Shift = '⇧';
    // „didžiosios lietuviškos raidės“: nuo šios pamokos žodžiuose ir sakiniuose gali būti Ą, Č, Š, Ž ... didžiosiomis
    public const char LtCapitals = '⇪';

    // Mokomų klavišų užrašai kortelėms ir pamokos įžangai
    public static IEnumerable<string> KeyLabels(Lesson lesson)
    {
        foreach (var key in lesson.NewKeys)
        {
            if (key == Shift)
                yield return "Shift";
            else if (key == LtCapitals)
                foreach (var letter in "ĄČĘĖĮŠŲŪŽ")
                    yield return letter.ToString();
            else
                yield return key.ToString().ToUpperInvariant();
        }
    }

    public static readonly IReadOnlyList<LessonModule> Modules;
    public static readonly IReadOnlyList<Lesson> All;

    static LessonCatalog()
    {
        // (modulio Id, pavadinimas, aprašas, ikona), po to pamokos: (Id, pavadinimas, naujos raidės, tipas, pagalba, pradinis lygis, pamokos aprašas)
        var defs = new List<(string Id, string Title, string Summary, string Icon, List<LessonSpec> Lessons)>
        {
            ("1", "Pagrindinė eilutė", "Pirštai ant A S D F ir J K L. Čia prasideda viskas.", "keyboard", new List<LessonSpec>
            {
                new("1-1", "F ir J: rodomieji pirštai", "fj", LessonKind.Drill, 2, 1,
                    "Padėk kairės rankos rodomąjį pirštą ant F, dešinės ant J. Ant šių klavišų yra mažos iškilusios žymės, todėl pirštai randa vietą nežiūrint. Žiūrėk į ekraną, ne į klaviatūrą."),
                new("1-2", "D ir K: viduriniai pirštai", "dk", LessonKind.Drill, 2, 1,
                    "Viduriniai pirštai guli ant D ir K. Po kiekvieno paspaudimo grąžink pirštą į pagrindinę padėtį."),
                new("1-3", "S ir L: bevardžiai pirštai", "sl", LessonKind.Drill, 2, 1,
                    "Bevardžiai pirštai silpnesni, todėl spausk lėtai ir tiksliai. Tikslumas svarbiau už greitį."),
                new("1-4", "A: mažasis pirštas", "a", LessonKind.Words, 2, 1,
                    "Kairės rankos mažasis pirštas spaudžia A. Dabar jau galima rašyti pirmuosius žodžius!"),
                new("1-5", "G ir H: rodomieji į šoną", "gh", LessonKind.Words, 2, 1,
                    "Rodomieji pirštai paspaudžia G ir H ir grįžta į F ir J. Nekelk delnų nuo stalo."),
                new("1-6", "Pagrindinė eilutė: kartojimas", "", LessonKind.Words, 1, 1,
                    "Visos pagrindinės eilutės raidės kartu. Jei jau jautiesi tvirtai, pamoka baigsis greitai."),
            }),
            ("2", "Viršutinė eilutė", "Pirštai kyla aukštyn ir grįžta į pagrindinę padėtį.", "target", new List<LessonSpec>
            {
                new("2-1", "E ir I: viduriniai pirštai aukštyn", "ei", LessonKind.Words, 2, 1,
                    "Viduriniai pirštai pasiekia viršutinę eilutę: kairysis E, dešinysis I. Po paspaudimo pirštas grįžta ant D arba K."),
                new("2-2", "O ir U", "ou", LessonKind.Words, 2, 1,
                    "O spaudžia dešinės rankos bevardis, U dešinės rankos rodomasis."),
                new("2-3", "R ir T: kairysis rodomasis", "rt", LessonKind.Words, 2, 1,
                    "R ir T spaudžia tas pats pirštas, kuris laiko F. Tiesk jį aukštyn ir sugrąžink."),
                new("2-4", "Y ir P", "yp", LessonKind.Words, 2, 1,
                    "Y spaudžia dešinės rankos rodomasis (tas, kuris laiko J), P dešinės rankos mažasis pirštas."),
                new("2-5", "Viršutinė eilutė: kartojimas", "", LessonKind.Words, 1, 1,
                    "Pagrindinė ir viršutinė eilutės kartu. Sekame tikslumą ir ritmą."),
            }),
            ("3", "Apatinė eilutė ir skyryba", "Pirštai lenkiasi žemyn. Pridedame kablelį ir tašką.", "chart", new List<LessonSpec>
            {
                new("3-1", "N ir V", "nv", LessonKind.Words, 2, 1,
                    "N spaudžia dešinės rankos rodomasis, V kairės rankos rodomasis. Pirštai lenkiasi žemyn, delnai lieka vietoje."),
                new("3-2", "M ir Z", "mz", LessonKind.Words, 2, 1,
                    "M spaudžia dešinės rankos rodomasis, Z kairės rankos mažasis pirštas."),
                new("3-3", "B ir C", "bc", LessonKind.Words, 2, 1,
                    "B spaudžia kairės rankos rodomasis, C kairės rankos vidurinis pirštas."),
                new("3-4", "Kablelis ir taškas", ",.", LessonKind.Words, 2, 1,
                    "Kablelį spaudžia dešinės rankos vidurinis pirštas, tašką bevardis. Jie rašomi iškart po žodžio, be tarpo."),
                new("3-5", "Apatinė eilutė: kartojimas", "", LessonKind.Words, 1, 1,
                    "Trys eilutės ir skyrybos ženklai kartu."),
            }),
            ("4", "Lietuviškos raidės", "Ą Č Ę Ė Į Š Ų Ū Ž yra skaičių eilutėje.", "star", new List<LessonSpec>
            {
                new("4-1", "Ė ir Ž", "ėž", LessonKind.Words, 2, 1,
                    "Lietuviškos raidės yra skaičių eilutėje: reikia ištiesti pirštą aukštyn ir grįžti atgal. Ė spaudžia kairės rankos rodomasis, Ž dešinės rankos mažasis pirštas."),
                new("4-2", "Š ir Ą", "šą", LessonKind.Words, 2, 1,
                    "Š spaudžia dešinės rankos rodomasis, Ą kairės rankos mažasis pirštas."),
                new("4-3", "Č ir Į", "čį", LessonKind.Words, 2, 1,
                    "Č spaudžia kairės rankos bevardis, Į kairės rankos rodomasis."),
                new("4-4", "Ų, Ę ir Ū", "ųęū", LessonKind.Words, 2, 1,
                    "Ų spaudžia dešinės rankos rodomasis, Ę kairės rankos vidurinis, Ū dešinės rankos vidurinis pirštas."),
                new("4-5", "Lietuviškos raidės: kartojimas", "", LessonKind.Words, 1, 2,
                    "Visos lietuviškos raidės kartu su visais išmoktais klavišais."),
            }),
            ("5", "Didžiosios raidės ir ženklai", "Shift, klaustukas, brūkšnelis ir tikri sakiniai.", "medal", new List<LessonSpec>
            {
                new("5-1", "Didžiosios raidės", Shift.ToString(), LessonKind.Words, 1, 1,
                    "Laikyk Shift priešingos rankos mažuoju pirštu: kairės rankos raidei dešinį Shift, dešinės rankos raidei kairįjį. Pirma nuspausk Shift, paskui raidę.", capitals: true),
                new("5-2", "Didžiosios lietuviškos raidės", LtCapitals.ToString(), LessonKind.Words, 2, 1,
                    "Lietuviškos didžiosios rašomos taip pat: laikyk Shift priešingos rankos mažuoju pirštu ir spausk raidę skaičių eilutėje. Pavyzdžiui, Š yra Shift ir 6 klavišas (dešinės rankos rodomasis). Jei vietoje raidės matai skaičių ar ženklą (! @ #), perjunk klaviatūrą į lietuvišką (Alt + Shift). Kai kuriuose išdėstymuose didžiąją raidę gausi su Caps Lock.", capitals: true),
                new("5-3", "Klaustukas ir brūkšnelis", "?-", LessonKind.Words, 1, 1,
                    "Klaustukas rašomas su Shift. Brūkšnelis yra šalia nulio, dešiniajame skaičių eilutės gale."),
                new("5-4", "Pirmieji sakiniai", "", LessonKind.Sentences, 1, 2,
                    "Tikri tekstai su didžiosiomis raidėmis, kableliais ir taškais."),
            }),
            ("6", "Ritmas ir greitis", "Tolygiai, be pauzių. Pirmieji 30 WPM.", "trophy", new List<LessonSpec>
            {
                new("6-1", "Dažni žodžiai", "", LessonKind.Words, 0, 2,
                    "Ritmas svarbiau už skubėjimą. Rašyk tolygiai, be pauzių tarp raidžių."),
                new("6-2", "Ilgesni žodžiai", "", LessonKind.Words, 0, 3,
                    "Ilgesni žodžiai moko pirštų kaitos. Nežiūrėk į klaviatūrą."),
                new("6-3", "Sakiniai", "", LessonKind.Sentences, 0, 2,
                    "Sakiniai su visomis išmoktomis raidėmis. Ištaisyk klaidą, bet nesustok."),
                new("6-4", "Pirmieji 30 WPM", "", LessonKind.Sentences, 0, 2,
                    "Pasiek 30 žodžių per minutę su ne mažesniu kaip 96 % tikslumu, kad gautum trečią žvaigždutę.", targetWpm: 30),
            }),
        };

        var modules = new List<LessonModule>();
        var all = new List<Lesson>();
        var allowed = "";
        foreach (var def in defs)
        {
            var lessons = new List<Lesson>();
            foreach (var spec in def.Lessons)
            {
                allowed += spec.NewKeys;
                var lesson = new Lesson
                {
                    Id = spec.Id,
                    Index = all.Count + lessons.Count,
                    ModuleId = def.Id,
                    Title = spec.Title,
                    Intro = spec.Intro,
                    NewKeys = spec.NewKeys,
                    AllowedKeys = allowed,
                    Kind = spec.Kind,
                    Hints = spec.Hints,
                    StartLevel = spec.StartLevel,
                    TargetWpm = spec.TargetWpm,
                    Capitals = spec.Capitals
                };
                lessons.Add(lesson);
            }
            all.AddRange(lessons);
            modules.Add(new LessonModule
            {
                Id = def.Id,
                Title = def.Title,
                Summary = def.Summary,
                Icon = def.Icon,
                Lessons = lessons
            });
        }
        Modules = modules;
        All = all;
    }

    public static Lesson Find(string id) => All.FirstOrDefault(lesson => lesson.Id == id);

    public static LessonModule ModuleOf(Lesson lesson) => Modules.First(module => module.Id == lesson.ModuleId);

    // Kita pamoka pagal eilę; null po paskutinės
    public static Lesson Next(Lesson lesson) => lesson.Index + 1 < All.Count ? All[lesson.Index + 1] : null;

    public static bool IsFree(Lesson lesson) => lesson.Index < FreeLessons;

    // Konstruktoriaus trumpinys katalogo sąrašui
    private sealed record LessonSpec(string Id, string Title, string NewKeys, LessonKind Kind, int Hints, int StartLevel,
        string Intro, int targetWpm = 0, bool capitals = false)
    {
        public int TargetWpm => targetWpm;
        public bool Capitals => capitals;
    }
}
