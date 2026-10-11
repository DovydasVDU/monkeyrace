# MonkeyRace

- UI taisyklės: žr. `DESIGN.md` (spalvos, šriftai, komponentai, NELIESTI sąrašas).
- Nekeisti JS naudojamų ID, `data-*` ir JS generuojamų klasių (`.correct`, `.char-wrong`, `.current`, `.char-extra` ir kt.).
- Jokių emoji UI – tik inline SVG (`_Icons`, `_Monkey`, `_Banana`).
- CSS tik per kintamuosius `var(--…)`, jokių hardcoded spalvų view'uose ir komponentuose.
- `git push` tik vartotojui paprašius.
