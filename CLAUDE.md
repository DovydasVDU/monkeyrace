# MonkeyRace

- UI taisyklės: žr. `DESIGN.md` (spalvos, šriftai, komponentai, NELIESTI sąrašas).
- Nekeisti JS naudojamų ID, `data-*` ir JS generuojamų klasių (`.correct`, `.char-wrong`, `.current`, `.char-extra` ir kt.).
- Jokių emoji UI – tik inline SVG (`_Icons`, `_Monkey`, `_Banana`).
- CSS tik per kintamuosius `var(--…)`, jokių hardcoded spalvų view'uose ir komponentuose.
- `git push` tik vartotojui paprašius.

## Ką sutvarkyti kitą kartą (audito liekanos, 2026-10-12)

Numeriai iš audito; 1–3 jau sutvarkyti (commit `00a87f1`).

- [ ] 4. `AuthController` Login/Register be bandymų ribojimo, slaptažodis gali būti 1 simbolio → `AddRateLimiter` + `[EnableRateLimiting]`, `password.Length >= 6`.
- [ ] 5. `RaceService._random` (`new Random()`) nesaugus kelioms gijoms → `Random.Shared`, lauką ištrinti.
- [ ] 6. `Startup.cs`: `UseExceptionHandler("/Home/Error")` užkomentuotas → produkcijoje tuščias 500 puslapis. Atkomentuoti.
- [ ] 7. Solo `RaceController.SaveResult` (anti-cheat, vienkartinė sesija) neturi testo → vienas `WebApplicationFactory` testas: per greitas 400, teisingas 200, antras siuntimas 400.
- [ ] 8. `renderWord`, WPM ir tikslumo skaičiavimas nukopijuoti `Play.cshtml` ir `MultiPlay.cshtml` → perkelti į `wwwroot/js/typing.js`.
- [ ] 9. `wwwroot/lib/` (bootstrap, jquery, jquery-validation, 9,6 MB) ir `_ValidationScriptsPartial.cshtml` niekur nenaudojami → ištrinti.
- [ ] 10. Du sprendimo failai: `dotnet build` šaknyje krenta (MSB1011) → ištrinti `monkeyrace.slnx` (`.sln` turi ir testus).
- [ ] 11. SignalR skriptas `_Layout.cshtml` kraunamas visuose puslapiuose ir `MultiPlay` dar kartą → ištrinti iš `_Layout`.
- [ ] 12. Nenaudojami `BotTypingModel` / `BotPlan.Model` ir `AddMemoryCache`, `AddHttpContextAccessor` (`Startup.cs`) → ištrinti.
- [ ] 13. `escapeHtml` kopijos (`Index.cshtml`, `MultiPlay.cshtml`) → naudoti `progressEscape` iš `progress.js`; ištrinti negyvą `Views/Home/Privacy.cshtml`.
- [ ] 14. `BotSkillProvider._guestWpm` auga iki perkrovimo → `shortcut:` komentaras arba laikyti sesijoje.
- [ ] Smulkmenos: solo `TimeSeconds`/`ErrorCount` netikrinami (gali būti neigiami); svečio vardas per `?username=` netikrinamas.
- [ ] Idėja: replay paspaudus rezultatą Top 10 → įrašyti lenktynių laiko juostą į DB, puslapis `/race/replay/{resultId}`, replay JS iškelti iš `MultiPlay.cshtml`. Veiktų tik naujoms lenktynėms.
- [ ] Laukia vartotojo testavimo: ar užtenka vieno sakinio teksto lenktynėms; ką daryti su flags `digits`, `many_caps`, `parens`; bananai žodžių režime nepriklauso nuo trukmės.
