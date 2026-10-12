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
| `--radius-card` | `24px` | |
| `--radius-ctl` | `16px` | |
| `--radius-pill` | `999px` | |
| `--border` | `2px solid var(--ink)` | |
| `--shadow-card` | `0 6px 0 rgba(0,0,0,.28)` | |
| `--focus` | `3px solid #FFF6DC` | `outline-offset: 3px` |

Taisyklė: komponentuose ir view'uose tik `var(--…)`, jokių hardcoded hex. Išimtis – SVG iliustracijų spalvos partial'uose (`_Monkey`, `_Banana`).

## Šriftai (Google Fonts, `_Layout` head, preconnect + `display=swap`)
- **Fredoka** 500/600/700 – antraštės, skaičiai, mygtukų pavadinimai.
- **Plus Jakarta Sans** 500–800 – visa kita.
- **JetBrains Mono** 500/600 – TIK lenktynių tekstas.
Visi trys turi latin-ext (ą č ę ė į š ų ū ž).

## Fonas
- `body`: `radial-gradient(900px 600px at 15% -5%, rgba(80,170,100,.35), transparent 60%)`, `radial-gradient(800px 600px at 95% 40%, rgba(255,201,60,.10), transparent 60%)` + 44×44 px tinklelis (1px linijos `rgba(255,246,220,.045)`).
- Dekoracijos – tik `Views/Shared/_Backdrop.cshtml` (absolute/fixed, `pointer-events:none`, `aria-hidden`, po turiniu): burbulai (kontūras arba rgba užpildas) ir „klavišai" Ą Č Ę Ė Į Š Ų Ū Ž (44–64 px, radius 14, 2px kontūras `rgba(255,246,220,.16)`, užpildas `rgba(255,246,220,.05)`, Fredoka raidė `rgba(255,246,220,.28)`, pasukimai −10..10°). Mobile ≤640px: tik 4–5.

## Komponentai (`site.css`)
- `.card` – cream fonas, `var(--border)`, radius 24, `--shadow-card`, ink tekstas. `.card--soon` – `#CBD9C4` (`--soon`), dashed kontūras.
- `.btn` – min-height 52px, radius 16, 2px ink kontūras, Plus Jakarta 800.
  - `.btn-p` – banana fonas, ink tekstas, `box-shadow: 0 5px 0 var(--banana-shadow)`, `:active { translateY(3px) }`.
  - `.btn-g` – skaidrus, cream 30% kontūras, cream tekstas (ant tamsaus).
  - `.btn-s` – skaidrus, ink kontūras (ant kreminės).
- `.chip` (ant tamsaus: `rgba(cream,.1)` fonas), `.pill` (ant šviesaus: cream fonas, 2px kontūras).
- `.tm` – pasirinkimo pill (15 s / 30 s / 1 min, greičiai), `.on` = ink fonas + banana tekstas.
- `.xp-track` – aukštis 14px, fonas `--cream-2`, 2px kontūras, užpildas `--leaf` (XP) arba `--banana` (lenktynių „Tu").
- Lenktynių takelis – 30px cream-2 pill, 2px kontūras, užpildas banana („Tu") / leaf-light (kiti), 32px beždžionė ant progreso galo; „Tu" beždžionė su žalia kepure.
- Lentelės eilutės – grid, dashed 2px skyrikliai `rgba(ink,.18)`; vietos 1/2/3 apskritimai (banana / `#D7D9D2` / `#E0A36A`) su 2px kontūru; vartotojo eilutė `--banana-soft`.
- Skaičiai – `font-variant-numeric: tabular-nums`.
- Tikslinis dydis ≥44px, `:focus-visible` visiems interaktyviems, `prefers-reduced-motion` išjungia animacijas.

## Iliustracijos
Tik inline SVG (`_Monkey`, `_Banana`, `_Icons`), jokių emoji. Beždžionė: ruda galva `#8B5E3C`, kreminis snukis `#F7DDB0`, taškinės akys, šypsena, uodega, ausys, kepurė (žalia `#2E8B4F`) pasirenkama parametru. Ikonos: 24px, stroke 2.2, round caps, `stroke="currentColor"`.

## Lenktynių tekstas (`race.css`)
JetBrains Mono, 24–26px desktop, 19px mobile, line-height 1.8–1.9, ant kreminės kortelės.
Būsenos (klasės generuoja JS, nekeisti): `.correct` (ink), `.char-wrong` (`--error` ant `--error-soft`, radius 4–6px), `.current` (banana-soft fonas + 3px leaf apatinis kontūras), neįvestos raidės opacity .4, `.char-extra`, `.char-expected`, `.word-input-error` (aiškus raudonas pabraukimas).

## NELIESTI (JS priklausomybės)
- **ID**: login.js – `loginModal`, `login-user`, `login-pass`, `userDropdown`; navbar – `quick-stats`; Index – `top10-body`; Play – `input-field`, `wpm-display`, `accuracy-display`, `timer-display`, `text-display`, `result-wpm`, `result-accuracy`, `result-time`, `result-klaidos`, `results-overlay`, `solo-reward`; MultiPlay – `input-field`, `queue-status`, `queue-meta`, `countdown`, `race-title`, `wpm-display`, `accuracy-display`, `timer-display`, `players-track`, `text-display`, `race-result`, `personal-result`, `personal-place`, `personal-wpm`, `personal-accuracy`, `personal-time`, `personal-errors`, `personal-reward`, `final-results`, `final-results-list`, `bot-explanation`, `result-leader-bar`, `result-history-status`, `result-overtakes-list`, `result-leader-summary`, `replay-race`, `replay-track`, `replay-range`, `replay-time`, `replay-play`, `replay-pause`, `replay-reset`, `replay-monkey-templates`, `result-wpm-chart`, `result-wpm-average`, `result-wpm-high`, `result-error-words`, `result-error-list`, `text-length`, `text-word-count`, `text-author`, `text-work`, `text-source`, `text-license`, `text-flags` (+ `-row`), `leave-button`; Index – `words-solo`, `words-online`; runtime – `track-{id}`, `wpm-{id}`, `typing-{id}`, `word-elem-{i}`.
- **Būsenos klasės**: `visible`, `active`, `current`, `correct`, `char-wrong`, `error` (ant `#input-field`), `word-input-error`, `card-open`, `reward-panel`.
- **JS generuojamos klasės**: `word`, `char`, `char-expected`, `char-extra`, `player-track`, `player-monkey`, `player-monkey-bot`, `player-monkey-human`, `player-name`, `player-wpm`, `player-card-pop`, `pc-head`, `xp-track`, `reward-*`, `level-badge`, `icon icon-*`, `visually-hidden`, `bot-badge`, `monkey-level`, `monkey-svg`, `podium-item`, `result-player-name`, `replay-*`, `control-icon`, `chart-*`, `word-fixed`, `word-wrong`.
- **data-\***: `data-word-index`, `data-replay-speed`, `data-replay-player`, `data-replay-track`, `data-replay-monkey`. **Custom prop**: `--progress`. **Atributai**: `[hidden]`, `aria-pressed`.
- **Struktūra**: `.modal` = pilno ekrano fonas, `.modal-box` jo vaikas; `#userDropdown` `.user-menu` viduje; `.reward-xp > span`; `#replay-monkey-templates` = 4 `_Monkey` su `.monkey-svg` (viewBox 160, lygio ženkliukas `cx=134`); `.char` `className` perrašomas JS – nedėti papildomų klasių.
- **Handlers**: `openLogin`, `closeLogin`, `login`, `register`, `openMenu`, `closeMenu`, `startSolo`, `leaveRace`, `restartRace`, `retrySameText`.
- `[hidden]` visada `display:none !important`.
