# MonkeyRace dizaino sistema („džiunglės")

Charakteris: šilta, žaisminga džiunglių tema. Tamsiai žalias fonas, kreminės kortelės, banano geltoni veiksmo mygtukai. Be šaltų pilkų atspalvių.

## Spalvos ir matmenys (`:root`, `wwwroot/css/site.css` viršuje)

| Kintamasis | Reikšmė | Paskirtis |
|---|---|---|
| `--bg` | `#123524` | puslapio fonas |
| `--bg-deep` | `#0F2E1E` | gilesnis fonas |
| `--cream` | `#FFF6DC` | kortelės |
| `--cream-2` | `#E9DDB8` | juostų fonas, takeliai |
| `--white` | `#fff` | |
| `--ink` | `#123524` | tekstas ant kreminės |
| `--text-on-dark` | `#FFF6DC` | tekstas ant tamsaus |
| `--muted-on-dark` | `#CFE3D0` | antrinis tekstas ant tamsaus |
| `--banana` | `#FFC93C` | pagrindinis veiksmas |
| `--banana-hover` | `#FFD45E` | |
| `--banana-shadow` | `#B98300` | mygtuko „3D" šešėlis |
| `--banana-soft` | `#FFE9A0` | paryškintos eilutės, pataisytos klaidos |
| `--leaf` | `#2E8B4F` | XP, sėkmė, „Tu" kepurė |
| `--leaf-light` | `#7FBF93` | kitų žaidėjų takeliai |
| `--brown` | `#8B5E3C` | beždžionė |
| `--error` | `#C93A3A` | klaida |
| `--error-soft` | `#FBE9E9` | klaidos fonas |
| `--water` / `--water-light` | `#2F8FD8` / `#7CC4F2` | parduotuvės vandens takelis (mėlyna – tik čia) |
| `--mask-banana` / `--mask-leaf` / `--mask-wave` | data-URI SVG | takelių formų kaukės (`mask-image`): tik silueto forma, spalvą duoda `background` su `var(--…)` |
| `--player-0`…`--player-3` | banana / `#E53935` / leaf / `#7E57C2` | perklausos žaidėjų spalvos (kepurės) |
| `--radius-card` | `24px` | |
| `--radius-ctl` | `16px` | |
| `--radius-pill` | `999px` | |
| `--border` | `2px solid var(--ink)` | |
| `--shadow-card` | `0 6px 0 rgba(0,0,0,.28)` | |
| `--focus` | `3px solid #FFF6DC` | `outline-offset: 3px` |

Taisyklė: komponentuose ir view'uose tik `var(--…)`, jokių hardcoded hex. Išimtis – SVG iliustracijų spalvos partial'uose (`_Monkey`, `_Banana`).

## Šriftai (lokalūs `wwwroot/fonts`, `@font-face` – `site.css` viršuje, `display: swap`; jokių Google Fonts / CDN)
- **Rubik** 500/600/700 (kintamas, 300–900) – antraštės, skaičiai, mygtukų pavadinimai. Anksčiau buvo Fredoka, bet ji neturi ą č ę ė į ū ų (naršyklė jas imdavo iš kito šrifto); Rubik turi visas lietuviškas raides.
- **Plus Jakarta Sans** 500–800 – visa kita.
- **JetBrains Mono** 500/600 – TIK lenktynių tekstas.
Visi trys turi latin-ext (ą č ę ė į š ų ū ž).

## Fonas
- `body`: `radial-gradient(900px 600px at 15% -5%, rgba(80,170,100,.35), transparent 60%)`, `radial-gradient(800px 600px at 95% 40%, rgba(255,201,60,.10), transparent 60%)` + 44×44 px tinklelis (1px linijos `rgba(255,246,220,.045)`).
- Dekoracijos – tik `Views/Shared/_Backdrop.cshtml` (absolute/fixed, `pointer-events:none`, `aria-hidden`, po turiniu): burbulai (kontūras arba rgba užpildas) ir „klavišai" Ą Č Ę Ė Į Š Ų Ū Ž (44–64 px, radius 14, 2px kontūras `rgba(255,246,220,.16)`, užpildas `rgba(255,246,220,.05)`, Rubik raidė `rgba(255,246,220,.28)`, pasukimai −10..10°). Mobile ≤640px: tik 4–5.

## Komponentai (`site.css`)
- `.card` – cream fonas, `var(--border)`, radius 24, `--shadow-card`, ink tekstas. `.card--soon` – `#CBD9C4` (`--soon`), dashed kontūras.
- `.btn` – min-height 52px, radius 16, 2px ink kontūras, Plus Jakarta 800.
  - `.btn-p` – banana fonas, ink tekstas, `box-shadow: 0 5px 0 var(--banana-shadow)`, `:active { translateY(3px) }`.
  - `.btn-g` – skaidrus, cream 30% kontūras, cream tekstas (ant tamsaus).
  - `.btn-s` – skaidrus, ink kontūras (ant kreminės).
- `.chip` (ant tamsaus: `rgba(cream,.1)` fonas), `.pill` (ant šviesaus: cream fonas, 2px kontūras).
- `.tm` – pasirinkimo pill (15 s / 30 s / 1 min, greičiai), `.on` = ink fonas + banana tekstas.
- `.xp-track` – aukštis 14px, fonas `--cream-2`, 2px kontūras, užpildas `--leaf` (XP) arba `--banana` (lenktynių „Tu").
- Parduotuvės takeliai (`ShopCatalog.Tracks`, klasė `.lane-<Value>` lenktynėse, `.track-<Value>` parduotuvės peržiūroje): `classic` – vientisas užpildas; `leaf` (lapeliai), `banana` (bananai), `water` (bangos) – užpildas iš pasikartojančių formų: `::after` su `mask-image: var(--mask-…)`, 26px aukščio, `width: var(--progress)`; beždžionė (`z-index: 1`) važiuoja virš užpildo.
- Pagrindinis puslapis: „Žaisti internetu“ (`.play-card`, kompaktiška) ir „Dažniausi žodžiai“ – vienoje eilutėje (`.home-play`); skyrius „Kiti žaidimai“: Žaisk su draugais, Žaisk su botais, Solo treniruotė, Turnyrai.
- Pamokų reklama: `.lesson-promo` (cream kortelė, per visą plotį) tarp `.home-play` ir „Kiti žaidimai“; naujokui – kvietimas pradėti, turinčiam progresą – kita pamoka ir `.xp-track`. Pirštų spalvų `kbd` (F J K L) naudoja `[data-finger]`. Profilyje – `.profile-lessons` blokas virš statistikos. Abu duomenis gauna iš `RaceService.GetLessonSummaryAsync`.
- Rašymo ekranai (lenktynės, solo, pamokos) kompiuteryje yra kompaktiški: viena viršutinė eilutė (Išeiti | pavadinimas | WPM·tikslumas·laikas), „Rašyk kuo greičiau!“ paslėpta, takelių eilutės po 38px, tekstas 24px 4 eilučių lange (JS slenka), pamokoje – žetonai ir atsiliepimas vienoje eilutėje, o žemame lange (≤780px) rankų paveikslėlis paslepiamas. Taisyklės – `race.css` ir `lessons.css` bloke „Kompaktas“; nedidinti tarpų, nes rašymo laukas turi matytis be slinkimo.
- Online lenktynių atgalinis skaičiavimas (`.countdown` MultiPlay): silpnas viso ekrano patamsinimas (`--scrim-soft`), skaičius – ink apskritime su banana tekstu, horizontaliai viduryje, vertikaliai takelių kortelės viduryje (JS `placeCountdown` rašo `--countdown-y`), kad neuždengtų teksto; tekstas keliamas virš patamsinimo (`.text-card` z-index), `pointer-events: none` – galima rašyti jau ties „GO!“.
- Profilio „Paskutinės lenktynės“: 10 eilučių, `#history-more` („Užkrauti daugiau“) kviečia `/User/PastRaces?skip=N`.
- Lenktynių takelis – 30px cream-2 pill, 2px kontūras, užpildas banana („Tu") / leaf-light (kiti), 32px beždžionė ant progreso galo; „Tu" beždžionė su žalia kepure.
- Lentelės eilutės – grid, dashed 2px skyrikliai `rgba(ink,.18)`; vietos 1/2/3 apskritimai (banana / `#D7D9D2` / `#E0A36A`) su 2px kontūru; vartotojo eilutė `--banana-soft`.
- Skaičiai – `font-variant-numeric: tabular-nums`.
- Tikslinis dydis ≥44px, `:focus-visible` visiems interaktyviems, `prefers-reduced-motion` išjungia animacijas.

## Iliustracijos
Tik inline SVG (`_Monkey`, `_Banana`, `_Icons`), jokių emoji. Beždžionė: ruda galva `#8B5E3C`, kreminis snukis `#F7DDB0`, taškinės akys, šypsena, uodega, ausys, kepurė (žalia `#2E8B4F`) pasirenkama parametru. Ikonos: 24px, stroke 2.2, round caps, `stroke="currentColor"`.

## Lenktynių tekstas (`race.css`)
JetBrains Mono, 24–26px desktop, 19px mobile, line-height 1.8–1.9, ant kreminės kortelės.
Būsenos (klasės generuoja JS, nekeisti): `.correct` (ink), `.char-wrong` (`--error` ant `--error-soft`, radius 4–6px), `.current` (banana-soft fonas + 3px leaf apatinis kontūras), neįvestos raidės opacity .4, `.char-extra`, `.char-expected`, `.word-input-error` (aiškus raudonas pabraukimas).

## Pamokos (`lessons.css`, `/pamokos`)
- **Pirštų spalvos** (`site.css :root`): `--finger-pinky` (= `--player-3`), `--finger-ring` (= `--water`), `--finger-middle` (= `--leaf`), `--finger-index` (= `--banana`), `--finger-thumb` (= `--brown`). `lessons.css` pagal `[data-finger]` nustato `--finger-color` ir `--finger-ink` (tekstas ant tos spalvos: baltas, ant banano – `--ink`). Ta pati `--finger-color` dažo klavišo apatinį kontūrą, paryškintą klavišą (`.is-target`) ir pirštą SVG rankose (`.is-active`).
- **Žemėlapis**: `.lesson-module` (antraštė su `.mode-icon`), `.lesson-grid` (auto-fill 190px), `.lesson-node` (kreminė kortelė; `.is-done` banana-soft, `.is-locked` kaip `.card--soon`), žvaigždutės `.lesson-star.is-on/.is-off` (`#icon-star`, išjungta – `grayscale + opacity`).
- **Grotuvas**: tekstas JetBrains Mono 30px (`.lw` žodis, `.lc` raidė: `.is-ok`, `.is-current`, `.is-miss`), `.lesson-chip` – klavišo meistriškumo juosta (`--fill` 0..1, `.is-mastered` leaf-light). Klaviatūra `.kb` (US forma, lietuviškos raidės skaičių eilutėje su mažu skaitmeniu `.kb-sub`), `.kb-home` – iškilusi žymė ant F ir J, `.is-new` banana-soft. Rankos – `_Hands.cshtml` SVG, ≤640px paslepiamos.
- Pagalbos lygiai: 2 = klaviatūra ir rankos, 1 = tik klaviatūra, 0 = nieko; moduliai nustato numatytąjį, žaidėjas keičia mygtuku (`localStorage`).
- Klaviatūros išdėstymas (`MR_LAYOUT` `lesson-keyboard.js`): US fizinės vietos, ą č ę ė į š ų ū ž skaičių eilutėje (ž ant `=`), `?` = Shift + `/`. Didžiosios lietuviškos raidės – Shift + ta pati skaičių eilutės klavišas (Š = Shift + 6); `!@#` tame pačiame klavise gaunami su Ctrl+Alt+Shift, todėl pamokose jų nemokome. Jei žaidėjų išdėstymas kitoks, keisti tik tą objektą.

## NELIESTI (JS priklausomybės)
- **ID**: login.js – `loginModal`, `login-user`, `login-pass`, `userDropdown`; navbar – `quick-stats`; Index – `top10-body`; Play – `input-field`, `wpm-display`, `accuracy-display`, `timer-display`, `text-display`, `result-wpm`, `result-accuracy`, `result-time`, `result-klaidos`, `results-overlay`, `solo-reward`; MultiPlay – `input-field`, `queue-status`, `queue-meta`, `countdown`, `race-title`, `wpm-display`, `accuracy-display`, `timer-display`, `players-track`, `text-display`, `race-result`, `personal-result`, `personal-place`, `personal-wpm`, `personal-accuracy`, `personal-time`, `personal-errors`, `personal-reward`, `final-results`, `final-results-list`, `bot-explanation`, `result-leader-bar`, `result-history-status`, `result-overtakes-list`, `result-leader-summary`, `replay-race`, `replay-track`, `replay-range`, `replay-time`, `replay-play`, `replay-pause`, `replay-reset`, `replay-monkey-templates`, `result-wpm-chart`, `result-wpm-average`, `result-wpm-high`, `result-error-words`, `result-error-list`, `text-length`, `text-word-count`, `text-author`, `text-work`, `text-source`, `text-license`, `text-flags` (+ `-row`), `leave-button`; Index – `words-solo`, `words-online`; runtime – `track-{id}`, `wpm-{id}`, `typing-{id}`, `word-elem-{i}`.
- **Būsenos klasės**: `visible`, `active`, `current`, `correct`, `char-wrong`, `error` (ant `#input-field`), `word-input-error`, `card-open`, `reward-panel`.
- **JS generuojamos klasės**: `word`, `char`, `char-expected`, `char-extra`, `player-track`, `player-monkey`, `player-monkey-bot`, `player-monkey-human`, `player-name`, `player-wpm`, `player-card-pop`, `pc-head`, `xp-track`, `reward-*`, `level-badge`, `icon icon-*`, `visually-hidden`, `bot-badge`, `monkey-level`, `monkey-svg`, `podium-item`, `result-player-name`, `replay-*`, `control-icon`, `chart-*`, `word-fixed`, `word-wrong`, `word-error` (rašomas žodis su klaida).
- **data-\***: `data-word-index`, `data-replay-speed`, `data-replay-player`, `data-replay-track`, `data-replay-monkey`. **Custom prop**: `--progress`. **Atributai**: `[hidden]`, `aria-pressed`.
- **Struktūra**: `.modal` = pilno ekrano fonas, `.modal-box` jo vaikas; `#userDropdown` `.user-menu` viduje; `.reward-xp > span`; `#replay-monkey-templates` = 4 `_Monkey` su `.monkey-svg` (viewBox 160, lygio ženkliukas `cx=134`); `.char` `className` perrašomas JS – nedėti papildomų klasių.
- **Handlers**: `openLogin`, `closeLogin`, `login`, `register`, `openMenu`, `closeMenu`, `startSolo`, `leaveRace`, `restartRace`, `retrySameText`.
- `[hidden]` visada `display:none !important`.
- **Pamokos**: ID `lesson-data` (JSON), `lesson-start`, `lesson-intro`, `lesson-stage`, `lesson-text`, `lesson-chips`, `lesson-feedback`, `lesson-keys`, `lesson-keyboard`, `lesson-hands`, `finger-caption`, `hint-toggle`, `result-*`, `lesson-reward`; `data-pace`, `data-finger`, `data-key`; JS klasės `lw`, `lc`, `lc-space`, `kb-key`, `kb-row`, `is-target`, `is-new`, `is-hit`, `is-miss`, `is-active`, `is-ok`, `is-current`, `is-mastered`; localStorage `monkeyrace-lessons`, `monkeyrace-lesson-prefs`.
