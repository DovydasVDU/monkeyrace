// Services/LessonEngine.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// Tempas: kiek tikslumo reikia ir kiek reikia pasitreniruoti, kol klavišas laikomas išmoktu
public sealed record LessonPace(string Name, double PassAccuracy, double MasteryMissRate, int MinAttempts, int RoundItems);

// Kliento suskaičiuota kiekvieno klavišo statistika (raktas – laukiamas simbolis; didžiosioms raidėms papildomai Shift ženklas)
public sealed class KeyPerf
{
    public int Attempts { get; set; }
    public int Misses { get; set; }
    public double TotalMs { get; set; }
}

public sealed class RoundPlan
{
    public bool Done { get; init; }
    public string Reason { get; init; } = "";
    public int Level { get; init; }
    public List<string> Items { get; init; } = new();
    // klavišai, kuriuos dabar treniruojame (rodomi žaidėjui)
    public string Focus { get; init; } = "";
}

// Adaptyvi pamokos logika be LLM: grynos funkcijos, todėl lengvai testuojamos.
// Serveris nieko neatsimena: naršyklė siunčia sukauptą būseną (raundą, lygį, klavišų statistiką) ir gauna kitą raundą.
public static class LessonEngine
{
    public const int MaxRounds = 6;
    public const int MaxLevel = 3;
    public const double EarlyFinishAccuracy = 97;
    public const double RaiseLevelAccuracy = 97;
    public const double LowerLevelAccuracy = 88;

    public static readonly LessonPace[] Paces =
    {
        new("ramiai", 90, 0.12, 8, 8),
        new("iprastai", 92, 0.08, 10, 10),
        new("greitai", 95, 0.05, 12, 12)
    };

    public static LessonPace Pace(string name) =>
        Paces.FirstOrDefault(p => p.Name == name) ?? Paces[1];

    // Žodžio ilgis pagal sunkumo lygį
    public static (int Min, int Max) LengthRange(int level) => Math.Clamp(level, 0, MaxLevel) switch
    {
        0 => (2, 5),
        1 => (3, 7),
        2 => (4, 9),
        _ => (5, 12)
    };

    public static int AdaptLevel(int level, double lastAccuracy, int round)
    {
        if (round <= 0)
            return level;
        if (lastAccuracy >= RaiseLevelAccuracy)
            return Math.Min(MaxLevel, level + 1);
        if (lastAccuracy < LowerLevelAccuracy)
            return Math.Max(0, level - 1);
        return level;
    }

    // Klavišai, kurie šioje pamokoje yra „fokusas“: naujos raidės (be skyrybos, jei yra raidžių) arba silpniausi
    public static string FocusKeys(Lesson lesson)
    {
        var letters = lesson.NewKeys.Where(char.IsLetter).ToArray();
        return letters.Length > 0 ? new string(letters) : lesson.NewKeys;
    }

    public static bool IsMastered(KeyPerf perf, LessonPace pace) =>
        perf != null && perf.Attempts >= pace.MinAttempts &&
        (double)perf.Misses / Math.Max(1, perf.Attempts + perf.Misses) <= pace.MasteryMissRate;

    // Ar po šio raundo pamoka baigta. Pirmiau už MaxRounds baigiama, kai visi nauji klavišai išmokti ir paskutinis raundas buvo labai tikslus.
    public static (bool Done, string Reason) IsDone(Lesson lesson, LessonPace pace, int roundsDone,
        double lastAccuracy, double lastWpm, IReadOnlyDictionary<string, KeyPerf> keys)
    {
        if (roundsDone <= 0)
            return (false, "");
        if (roundsDone >= MaxRounds)
            return (true, "max");

        if (lesson.TargetWpm > 0)
        {
            // tikslinio greičio pamoka baigiasi tik pasiekus tikslą arba pasibaigus raundams
            return lastWpm >= lesson.TargetWpm && lastAccuracy >= 96 ? (true, "target") : (false, "");
        }

        if (lastAccuracy < EarlyFinishAccuracy)
            return (false, "");

        if (lesson.IsReview || lesson.Kind == LessonKind.Sentences)
            return roundsDone >= 2 ? (true, "early") : (false, "");

        var focus = lesson.Capitals ? new[] { LessonCatalog.Shift.ToString() } :
            FocusKeys(lesson).Select(c => c.ToString()).ToArray();
        var mastered = focus.All(key => keys != null && keys.TryGetValue(key, out var perf) && IsMastered(perf, pace));
        return mastered ? (true, "early") : (false, "");
    }

    public static int StarsFor(Lesson lesson, LessonPace pace, double accuracy, double wpm)
    {
        if (accuracy < pace.PassAccuracy)
            return 0;
        var stars = accuracy >= 98 ? 3 : accuracy >= 95 ? 2 : 1;
        // trečia žvaigždutė tikslinio greičio pamokoje tik pasiekus tikslą
        if (lesson.TargetWpm > 0 && wpm < lesson.TargetWpm)
            stars = Math.Min(stars, 2);
        return stars;
    }

    // Kitas raundas. recent – žodžiai, jau rodyti šioje pamokoje (kad nesikartotų). sentences – paruošti sakiniai iš tekstų (gali būti tušti).
    public static RoundPlan NextRound(Lesson lesson, LessonPace pace, int roundsDone, int level, double lastAccuracy,
        double lastWpm, IReadOnlyDictionary<string, KeyPerf> keys, IReadOnlyCollection<string> recent,
        IReadOnlyList<string> sentences, Random rng)
    {
        var done = IsDone(lesson, pace, roundsDone, lastAccuracy, lastWpm, keys);
        if (done.Done)
            return new RoundPlan { Done = true, Reason = done.Reason, Level = level };

        level = Math.Clamp(AdaptLevel(level, lastAccuracy, roundsDone), 0, MaxLevel);
        var focus = FocusKeys(lesson);
        var weak = WeakKeys(lesson, keys, pace);
        var plan = new RoundPlan { Level = level, Focus = focus.Length > 0 ? focus : new string(weak.ToArray()) };

        if (lesson.Kind == LessonKind.Sentences)
        {
            plan.Items.AddRange(SentenceItems(lesson, level, Math.Max(2, pace.RoundItems / 3), sentences, recent, rng));
            return plan;
        }

        plan.Items.AddRange(WordItems(lesson, level, pace.RoundItems, weak, recent, rng));
        return plan;
    }

    // Silpniausi jau išmokti klavišai (didelis klaidų procentas) – kartojami visose pamokose
    public static List<char> WeakKeys(Lesson lesson, IReadOnlyDictionary<string, KeyPerf> keys, LessonPace pace)
    {
        var weak = new List<char>();
        if (keys == null)
            return weak;
        foreach (var (key, perf) in keys.OrderByDescending(p => MissRate(p.Value)))
        {
            if (key.Length != 1 || perf.Attempts < 3 || MissRate(perf) < 0.15)
                continue;
            var letter = char.ToLowerInvariant(key[0]);
            if (char.IsLetter(letter) && lesson.AllowedKeys.Contains(letter) && !weak.Contains(letter))
                weak.Add(letter);
            if (weak.Count >= 3)
                break;
        }
        return weak;
    }

    private static double MissRate(KeyPerf perf) =>
        perf == null ? 0 : (double)perf.Misses / Math.Max(1, perf.Attempts + perf.Misses);

    // ---------- žodžiai ----------

    public static List<string> WordItems(Lesson lesson, int level, int count, IReadOnlyCollection<char> weak,
        IReadOnlyCollection<string> recent, Random rng)
    {
        var letters = new HashSet<char>(lesson.AllowedKeys.Where(char.IsLetter));
        var punct = lesson.AllowedKeys.Where(c => ",.?-".Contains(c)).ToArray();
        var capitals = lesson.AllowedKeys.Contains(LessonCatalog.Shift);
        var focus = FocusKeys(lesson).Where(char.IsLetter).ToArray();
        var recentSet = new HashSet<string>((recent ?? Array.Empty<string>()).Select(Normalize), StringComparer.Ordinal);
        var (min, max) = LengthRange(level);

        bool Typeable(string word) => word.All(c => letters.Contains(char.ToLowerInvariant(c)));
        bool InRange(string word, int slack) => word.Length >= min - slack && word.Length <= max + slack;

        // žodžių telkiniai: pagrindinis (turi fokuso raidžių) ir kartojimo (turi silpnų raidžių)
        List<string> Pool(IEnumerable<char> mustHave, int slack)
        {
            var must = mustHave.ToArray();
            return LessonWords.Lower.Where(w => Typeable(w) && InRange(w, slack) &&
                (must.Length == 0 || w.Any(c => must.Contains(c))) && !recentSet.Contains(w)).ToList();
        }

        // pamoka su fokuso raidėmis: žodžiai su jomis; kartojimo pamoka: žodžiai su silpnomis raidėmis arba bet kokie
        var mustFocus = focus.Length > 0 ? focus : weak.ToArray();
        var main = Pool(mustFocus, 0);
        for (var slack = 1; main.Count < count && slack <= 4; slack++)
            main = Pool(mustFocus, slack);
        // žodžių su fokuso raidėmis per mažai (pirmos pamokos) – nedidelis telkinys kartojamas ir papildomas raidžių grupėmis
        var items = new List<string>();
        var weakPool = weak.Count > 0 && focus.Length > 0 ? Pool(weak, 1) : new List<string>();

        var used = new HashSet<string>();

        // iš 3 atsitiktinių žodžių renkamasi tas, kuriame daugiau fokuso raidžių (jos treniruojamos dažniau)
        string PickWeighted(List<string> pool)
        {
            if (pool.Count == 0)
                return null;
            string best = null;
            var bestScore = double.MinValue;
            for (var i = 0; i < 6 && best == null || i < 3; i++)
            {
                var candidate = pool[rng.Next(pool.Count)];
                if (used.Contains(candidate) && pool.Count > count)
                    continue;
                var score = candidate.Count(c => mustFocus.Contains(c)) - candidate.Length * 0.05;
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        var drillOnly = lesson.Kind == LessonKind.Drill;
        // Capitals pamokoje didžioji raidė – pagrindinis tikslas
        var mixNames = capitals ? LessonWords.Names.Where(name => Typeable(name) && IsBasicLatin(name[0])).ToList() : new List<string>();
        var guard = 0;
        while (items.Count < count && guard++ < count * 8)
        {
            string word = null;
            if (!drillOnly)
            {
                // ~25 % elementų – silpnų raidžių kartojimas
                var pool = weakPool.Count > 0 && rng.NextDouble() < 0.25 ? weakPool : main;
                word = PickWeighted(pool);
                if (word == null && main.Count > 0)
                    word = main[rng.Next(main.Count)];
            }
            // per mažas telkinys (arba drill pamoka): raidžių grupė
            if (word == null || (main.Count < 4 && rng.NextDouble() < 0.6) || drillOnly)
                word = Drill(lesson, focus, level, rng);

            word = Decorate(word, lesson, punct, capitals, mixNames, rng);
            if (word.Length == 0 || (used.Contains(Normalize(word)) && main.Count >= count))
                continue;
            used.Add(Normalize(word));
            items.Add(word);
        }
        return items;
    }

    // Elementas taip, kaip jis rašomas („Kasa,“, „kas-kas“), -> žodis be didžiosios raidės ir skyrybos, kad kartotis nebūtų
    public static string Normalize(string item) =>
        (item ?? "").Split('-')[0].TrimEnd(',', '.', '?').ToLowerInvariant();

    private const string Vowels = "aeiyouąęėįųū";

    // Žodžių neužtenka (pačios pirmos pamokos): raidžių grupės. Kai yra ir balsių, ir priebalsių, grupės tariamos
    // (priebalsis-balsis, pvz. „gaha“), kitaip – atsitiktinės raidės; ta pati raidė nesikartoja daugiau nei du kartus iš eilės.
    public static string Drill(Lesson lesson, char[] focus, int level, Random rng)
    {
        var alphabet = lesson.AllowedKeys.Where(char.IsLetter).Distinct().ToArray();
        if (alphabet.Length == 0)
            return "";
        var vowels = alphabet.Where(c => Vowels.Contains(c)).ToArray();
        var consonants = alphabet.Where(c => !Vowels.Contains(c)).ToArray();
        var pronounceable = lesson.Kind != LessonKind.Drill && vowels.Length > 0 && consonants.Length > 0;
        var (min, max) = LengthRange(level);
        var length = rng.Next(Math.Max(3, min), Math.Max(4, Math.Min(max, 6)) + 1);
        var startWithVowel = rng.Next(4) == 0;
        var sb = new StringBuilder();
        for (var i = 0; i < length; i++)
        {
            var source = pronounceable ? ((i % 2 == 0) == startWithVowel ? vowels : consonants) : alphabet;
            char next;
            var attempts = 0;
            do
            {
                // pusė raidžių – nauji klavišai, kad jie būtų treniruojami dažniau
                var focusHere = focus.Where(c => source.Contains(c)).ToArray();
                next = focusHere.Length > 0 && rng.Next(2) == 0 ? focusHere[rng.Next(focusHere.Length)] : source[rng.Next(source.Length)];
            } while (attempts++ < 12 && sb.Length >= 2 && sb[^1] == next && sb[^2] == next);
            sb.Append(next);
        }
        // bent viena fokuso raidė
        if (focus.Length > 0 && !sb.ToString().Any(c => focus.Contains(c)))
            sb[rng.Next(sb.Length)] = focus[rng.Next(focus.Length)];
        return sb.ToString();
    }

    // Didžiąsias lietuviškas raides (Š, Ž ...) kai kuriuose išdėstymuose reikia rinkti kitaip (Caps Lock), todėl
    // Shift pamokose didžiosios tik a-z
    private static bool IsBasicLatin(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    // Didžioji raidė, skyryba: kad žodis atrodytų kaip tekste
    public static string Decorate(string word, Lesson lesson, char[] punct, bool capitals, List<string> names, Random rng)
    {
        if (word.Length == 0)
            return word;
        if (capitals)
        {
            // Shift pamokoje didžiosios dažnos, vėlesnėse – retesnės
            var chance = lesson.Capitals ? 0.7 : 0.2;
            if (rng.NextDouble() < chance)
            {
                if (names.Count > 0 && rng.NextDouble() < 0.5)
                    word = names[rng.Next(names.Count)];
                else if (IsBasicLatin(word[0]))
                    word = char.ToUpperInvariant(word[0]) + word.Substring(1);
            }
        }
        // paskutinė skyrybos pamoka dažniau dėkoja ženklui, kuris mokomas
        if (punct.Length > 0)
        {
            var taught = lesson.NewKeys.Where(c => punct.Contains(c)).ToArray();
            var chance = taught.Length > 0 ? 0.7 : 0.2;
            if (rng.NextDouble() < chance)
            {
                var sign = (taught.Length > 0 ? taught : punct)[rng.Next(taught.Length > 0 ? taught.Length : punct.Length)];
                word = sign == '-' ? word + "-" + word[..Math.Min(word.Length, 3)] : word + sign;
            }
        }
        return word;
    }

    // ---------- sakiniai ----------

    private static readonly Regex SentenceSplit = new(@"(?<=[.?!])\s+", RegexOptions.Compiled);

    // Iš teksto ištraukia sakinius, kuriuos galima parašyti su šios pamokos klavišais
    public static List<string> ExtractSentences(Lesson lesson, IEnumerable<string> texts, int maxLength = 120)
    {
        var result = new List<string>();
        foreach (var text in texts)
        {
            foreach (var raw in SentenceSplit.Split(text ?? ""))
            {
                var sentence = raw.Trim();
                if (sentence.Length is >= 20 && sentence.Length <= maxLength && IsTypeable(lesson, sentence))
                    result.Add(sentence);
            }
        }
        return result;
    }

    public static bool IsTypeable(Lesson lesson, string text)
    {
        var capitals = lesson.AllowedKeys.Contains(LessonCatalog.Shift);
        foreach (var c in text)
        {
            if (c == ' ')
                continue;
            if (char.IsLetter(c))
            {
                if (char.IsUpper(c) && (!capitals || !IsBasicLatin(c)))
                    return false;
                if (!lesson.AllowedKeys.Contains(char.ToLowerInvariant(c)))
                    return false;
                continue;
            }
            if (!lesson.AllowedKeys.Contains(c))
                return false;
        }
        return true;
    }

    public static List<string> SentenceItems(Lesson lesson, int level, int count, IReadOnlyList<string> sentences,
        IReadOnlyCollection<string> recent, Random rng)
    {
        var maxLen = 50 + level * 25;
        var recentSet = new HashSet<string>(recent ?? Array.Empty<string>(), StringComparer.Ordinal);
        var pool = (sentences ?? Array.Empty<string>())
            .Where(s => s.Length <= maxLen && !recentSet.Contains(s)).Distinct().ToList();
        var items = new List<string>();
        while (items.Count < count && pool.Count > 0)
        {
            var i = rng.Next(pool.Count);
            items.Add(pool[i]);
            pool.RemoveAt(i);
        }
        // sakinių per mažai: žodžių eilutės su didžiąja raide ir tašku
        var guard = 0;
        while (items.Count < count && guard++ < count * 6)
        {
            var words = WordItems(lesson, level, 4 + level, Array.Empty<char>(), recent, rng)
                .Select(w => w.TrimEnd(',', '.', '?', '-')).Where(w => w.Length > 0).ToList();
            if (words.Count == 0)
                break;
            var line = string.Join(" ", words);
            line = (IsBasicLatin(line[0]) ? char.ToUpperInvariant(line[0]) : line[0]) + line.Substring(1) + ".";
            if (!lesson.AllowedKeys.Contains('.'))
                line = line.TrimEnd('.');
            if (!lesson.AllowedKeys.Contains(LessonCatalog.Shift))
                line = line.ToLowerInvariant();
            items.Add(line);
        }
        return items;
    }
}
