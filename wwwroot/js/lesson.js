// Pamokos grotuvas (/pamokos/{id}). Serveris nieko neatsimena: čia laikoma raundų būsena (klavišų statistika, lygis),
// po kiekvieno raundo siunčiama į POST /pamokos/{id}/round ir gaunami kiti žodžiai arba "done".
(() => {
    const data = JSON.parse(document.getElementById("lesson-data").textContent);
    const lesson = data.lesson;
    const $ = id => document.getElementById(id);
    const SHIFT = "⇧";
    const LT_CAPS = "⇪"; // pamoka „didžiosios lietuviškos raidės“ (LessonCatalog.LtCapitals)
    const PREF_KEY = "monkeyrace-lesson-prefs";
    const GUEST_KEY = "monkeyrace-lessons";

    function readJson(key, fallback) {
        try { return JSON.parse(localStorage.getItem(key)) || fallback; } catch { return fallback; }
    }
    function writeJson(key, value) {
        try { localStorage.setItem(key, JSON.stringify(value)); } catch { /* privatus langas */ }
    }

    const prefs = readJson(PREF_KEY, {});
    let pace = data.paces.find(item => item.name === prefs.pace) || data.paces[1];
    let hints = Number.isInteger(prefs.hints) ? prefs.hints : lesson.hints;
    const HINT_LABELS = ["Be pagalbos", "Tik klaviatūra", "Klaviatūra ir rankos"];

    const els = {
        intro: $("lesson-intro"), stage: $("lesson-stage"), text: $("lesson-text"), chips: $("lesson-chips"),
        feedback: $("lesson-feedback"), keyboard: $("lesson-keyboard"), keysWrap: $("lesson-keys"),
        hands: $("lesson-hands"), caption: $("finger-caption"), wpm: $("wpm-display"), acc: $("accuracy-display"),
        round: $("round-display"), hintBtn: $("hint-toggle"), start: $("lesson-start"), overlay: $("results-overlay")
    };

    // ---------- pasirinkimai ----------

    function applyHints() {
        els.keysWrap.hidden = hints === 0;
        els.keysWrap.classList.toggle("no-hands", hints < 2);
        els.hintBtn.textContent = `Pagalba: ${HINT_LABELS[hints]}`;
        if (hints === 0) LessonKeyboard.clearTargets();
        else if (active && pos < text.length) LessonKeyboard.highlight(text[pos]);
    }

    els.hintBtn.addEventListener("click", () => {
        hints = (hints + 1) % 3;
        prefs.hints = hints;
        writeJson(PREF_KEY, prefs);
        applyHints();
    });

    document.querySelectorAll("[data-pace]").forEach(button => {
        button.setAttribute("aria-pressed", String(button.dataset.pace === pace.name));
        button.addEventListener("click", () => {
            pace = data.paces.find(item => item.name === button.dataset.pace) || pace;
            prefs.pace = pace.name;
            writeJson(PREF_KEY, prefs);
            document.querySelectorAll("[data-pace]").forEach(other =>
                other.setAttribute("aria-pressed", String(other === button)));
        });
    });

    // ---------- bėgimo būsena ----------

    let run = null;
    let tracker = null;
    let active = false;
    let text = "";
    let pos = 0;
    let cells = [];
    let tokenStart = 0;
    let tokenError = false;
    let roundErrors = 0;
    let roundStart = 0;
    let lastKeyAt = 0;
    let busy = false;
    // žodžiai į klaidų statistiką tik iš didelių žodžių telkinių (pačios pirmos pamokos naudoja raidžių grupes)
    const trackWords = lesson.kind !== "Drill" && lesson.index >= 6;

    function newRun() {
        run = {
            round: 0, level: lesson.startLevel, keys: {}, recent: [], rounds: [],
            lastAcc: 100, lastWpm: 0, hits: 0, errors: 0
        };
        tracker = createMistakeTracker();
    }

    function perfOf(key) {
        return run.keys[key] || (run.keys[key] = { attempts: 0, misses: 0, totalMs: 0 });
    }

    function isUpper(ch) {
        return ch !== ch.toLowerCase();
    }

    // ---------- pagrindiniai klavišai (fokusas) ----------

    const focusKeys = lesson.newKeys.includes(SHIFT) || lesson.newKeys.includes(LT_CAPS) ? [SHIFT]
        : [...lesson.newKeys.replace(/[^\p{L}]/gu, "")].length > 0
            ? [...lesson.newKeys.replace(/[^\p{L}]/gu, "")] : [...lesson.newKeys];

    function isMastered(perf) {
        if (!perf || perf.attempts < pace.minAttempts) return false;
        return perf.misses / Math.max(1, perf.attempts + perf.misses) <= pace.masteryMissRate;
    }

    function renderChips() {
        els.chips.textContent = "";
        els.chips.hidden = focusKeys.length === 0 || lesson.kind === "Sentences";
        focusKeys.forEach(key => {
            const chip = document.createElement("span");
            chip.className = "lesson-chip";
            chip.dataset.key = key;
            chip.innerHTML = `<b>${key === SHIFT ? "Shift" : key.toUpperCase()}</b><i></i>`;
            els.chips.appendChild(chip);
        });
        updateChips();
    }

    function updateChips() {
        els.chips.querySelectorAll(".lesson-chip").forEach(chip => {
            const perf = run && run.keys[chip.dataset.key];
            const fill = perf ? Math.min(1, perf.attempts / pace.minAttempts) : 0;
            chip.style.setProperty("--fill", fill);
            chip.classList.toggle("is-mastered", isMastered(perf));
        });
    }

    // ---------- raundai ----------

    function setFeedback(message) {
        els.feedback.textContent = message || "";
    }

    async function fetchRound() {
        busy = true;
        setFeedback("");
        try {
            const response = await fetch(`/pamokos/${lesson.id}/round`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    pace: pace.name, round: run.round, level: run.level, lastAccuracy: run.lastAcc,
                    lastWpm: run.lastWpm, keys: run.keys, recent: run.recent.slice(-120)
                })
            });
            if (response.status === 401) {
                showLocked();
                return;
            }
            if (!response.ok) throw new Error("round");
            const plan = await response.json();
            if (plan.done) {
                await finish(plan.reason);
                return;
            }
            if (run.round > 0) {
                if (plan.level > run.level) setFeedback("Puikiai sekasi! Žodžiai tampa sunkesni.");
                else if (plan.level < run.level) setFeedback("Lėčiau ir tiksliau. Žodžiai šiek tiek lengvesni.");
            }
            run.level = plan.level;
            startRound(plan.items);
        } catch {
            setFeedback("Nepavyko gauti žodžių. Patikrink ryšį ir paspausk bet kurį klavišą, kad bandytum dar kartą.");
            retryOnKey = true;
        } finally {
            busy = false;
        }
    }

    let retryOnKey = false;

    function showLocked() {
        setFeedback("Toliau reikia paskyros.");
        if (typeof openLogin === "function") openLogin();
    }

    function startRound(items) {
        text = items.join(" ");
        pos = 0;
        roundErrors = 0;
        roundStart = 0;
        tokenStart = 0;
        tokenError = false;
        buildText(text);
        els.round.textContent = String(run.round + 1);
        run.pendingItems = items;
        active = true;
        LessonKeyboard.clearTargets();
        markCurrent();
    }

    function buildText(value) {
        els.text.textContent = "";
        cells = [];
        const tokens = value.split(" ");
        tokens.forEach((token, tokenIndex) => {
            const word = document.createElement("span");
            word.className = "lw";
            for (const ch of token) {
                const cell = document.createElement("span");
                cell.className = "lc";
                cell.textContent = ch;
                word.appendChild(cell);
                cells.push(cell);
            }
            if (tokenIndex < tokens.length - 1) {
                const space = document.createElement("span");
                space.className = "lc lc-space";
                space.textContent = " ";
                word.appendChild(space);
                cells.push(space);
            }
            els.text.appendChild(word);
        });
    }

    function markCurrent() {
        cells.forEach(cell => cell.classList.remove("is-current"));
        if (pos < cells.length) {
            cells[pos].classList.add("is-current");
            if (hints > 0) LessonKeyboard.highlight(text[pos]);
        }
    }

    function updateStats() {
        const hits = run.hits + pos;
        const errors = run.errors + roundErrors;
        els.acc.textContent = String(Math.round(hits + errors === 0 ? 100 : hits * 100 / (hits + errors)));
        const elapsed = performance.now() - roundStart;
        els.wpm.textContent = roundStart && pos > 4 && elapsed > 0 ? String(Math.round(pos / 5 / (elapsed / 60000))) : els.wpm.textContent;
    }

    function onKeyDown(event) {
        if (!run || event.isComposing) return;
        const altGr = event.getModifierState && event.getModifierState("AltGraph");
        if ((event.ctrlKey || event.metaKey || event.altKey) && !altGr) return;
        if (event.key.length !== 1) return;
        if (!active) {
            if (retryOnKey && !busy) {
                retryOnKey = false;
                event.preventDefault();
                fetchRound();
            }
            return;
        }
        event.preventDefault();
        const now = performance.now();
        if (!roundStart) {
            roundStart = now;
            lastKeyAt = now;
        }
        const expected = text[pos];
        if (event.key === expected) hit(expected, now);
        else miss(expected, event.key);
    }

    function hit(expected, now) {
        if (expected !== " ") {
            const perf = perfOf(expected.toLowerCase());
            perf.attempts++;
            perf.totalMs += Math.min(3000, Math.max(0, now - lastKeyAt));
            if (isUpper(expected)) perfOf(SHIFT).attempts++;
        }
        lastKeyAt = now;
        cells[pos].classList.remove("is-miss");
        cells[pos].classList.add("is-ok");
        LessonKeyboard.flash(expected, "ok");
        pos++;

        // žodis baigtas
        if (pos >= text.length || text[pos] === " ") {
            if (trackWords) tracker.word(text.slice(tokenStart, pos), tokenError);
            tokenStart = pos + 1;
            tokenError = false;
        }
        updateChips();
        updateStats();
        if (pos >= text.length) endRound(now);
        else markCurrent();
    }

    function miss(expected, typed) {
        roundErrors++;
        tokenError = true;
        if (expected !== " ") {
            const lower = expected.toLowerCase();
            perfOf(lower).misses++;
            // užmiršta Shift: paspausta ta pati raidė mažoji
            if (isUpper(expected)) perfOf(SHIFT).misses++;
        }
        tracker.key(expected, typed);
        cells[pos].classList.add("is-miss");
        LessonKeyboard.flash(typed, "miss");
        if (isUpper(expected) && typed === expected.toLowerCase())
            setFeedback("Pirma nuspausk Shift, laikyk jį ir tada spausk raidę.");
        else if (LessonKeyboard.looksLikeWrongLayout(expected, typed))
            setFeedback(isUpper(expected)
                ? "Vietoje raidės išėjo skaičius ar ženklas. Perjunk klaviatūrą į lietuvišką (Alt + Shift) ir bandyk dar kartą."
                : "Atrodo, įjungta ne lietuviška klaviatūra. Perjunk išdėstymą (Alt + Shift) ir bandyk dar kartą.");
        updateChips();
        updateStats();
    }

    function endRound(now) {
        active = false;
        const elapsed = Math.max(500, Math.round(now - roundStart));
        const chars = text.length;
        const accuracy = chars * 100 / (chars + roundErrors);
        const wpm = chars / 5 / (elapsed / 60000);
        run.rounds.push({ chars, errors: roundErrors, elapsedMs: elapsed });
        run.hits += chars;
        run.errors += roundErrors;
        run.round++;
        run.lastAcc = accuracy;
        run.lastWpm = wpm;
        run.recent.push(...run.pendingItems);
        els.wpm.textContent = String(Math.round(wpm));
        LessonKeyboard.clearTargets();
        setFeedback(`Raundas baigtas: ${Math.round(accuracy)} % tikslumas, ${Math.round(wpm)} WPM.`);
        setTimeout(fetchRound, 900);
    }

    // ---------- pabaiga ----------

    async function finish(reason) {
        active = false;
        els.stage.hidden = true;
        let result = null;
        const guestSaved = readJson(GUEST_KEY, {});
        try {
            const response = await fetch(`/pamokos/${lesson.id}/complete`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    pace: pace.name,
                    previousStars: data.loggedIn ? data.bestStars : ((guestSaved[lesson.id] || {}).stars || 0),
                    rounds: run.rounds
                })
            });
            if (response.status === 401) {
                showLocked();
                return;
            }
            if (response.ok) result = await response.json();
        } catch { /* parodysime be įskaitymo */ }

        if (data.loggedIn) tracker.send();
        if (result && !result.saved && result.stars > 0) {
            const old = guestSaved[lesson.id] || { stars: 0, accuracy: 0, wpm: 0 };
            guestSaved[lesson.id] = {
                stars: Math.max(old.stars, result.stars),
                accuracy: Math.max(old.accuracy, result.accuracy),
                wpm: Math.max(old.wpm, result.wpm)
            };
            writeJson(GUEST_KEY, guestSaved);
        }
        showResults(result, reason);
    }

    function showResults(result, reason) {
        const stars = result ? result.stars : 0;
        $("result-stars").innerHTML = [1, 2, 3].map(index =>
            `<svg class="ui-icon lesson-star ${index <= stars ? "is-on" : "is-off"}" aria-hidden="true"><use href="#icon-star"></use></svg>`).join("");
        $("result-stars").setAttribute("aria-label", `${stars} iš 3 žvaigždučių`);
        const passAccuracy = pace.passAccuracy;
        let title;
        let message;
        if (!result) {
            title = "Rezultato išsaugoti nepavyko";
            message = "Patikrink ryšį ir pabandyk dar kartą.";
        } else if (stars === 0) {
            title = "Dar kartelį!";
            message = `Reikia bent ${passAccuracy} % tikslumo. Rašyk lėčiau ir žiūrėk į ekraną, o ne į klaviatūrą.`;
        } else if (stars === 3) {
            title = "Puikiai!";
            message = reason === "early" ? "Klavišus jau gerai moki, todėl pamoką užbaigėme greičiau." : "Beveik be klaidų.";
        } else {
            title = "Pamoka įveikta!";
            message = stars === 2 ? "Dar šiek tiek tikslumo ir bus trys žvaigždutės." : "Pabandyk dar kartą, kad gautum daugiau žvaigždučių.";
        }
        if (result && lesson.targetWpm > 0 && result.wpm < lesson.targetWpm && stars >= 1)
            message = `Trečiai žvaigždutei reikia ${lesson.targetWpm} WPM, tu parašei ${result.wpm}.`;
        $("result-title").textContent = title;
        $("result-message").textContent = message;
        $("result-accuracy").textContent = result ? String(Math.round(result.accuracy * 10) / 10) : "0";
        $("result-wpm").textContent = result ? String(result.wpm) : "0";
        $("result-rounds").textContent = String(run.rounds.length);
        if (result && result.stars > 0) renderReward($("lesson-reward"), result.reward);
        else $("lesson-reward").hidden = true;

        const actions = $("result-actions");
        actions.textContent = "";
        const retry = button("btn btn-s", "icon-redo", "Kartoti", () => location.reload());
        const map = link("btn btn-s", "icon-home", "Pamokų žemėlapis", "/pamokos");
        if (result && stars > 0 && lesson.nextId) {
            if (lesson.nextFree || data.loggedIn)
                actions.append(link("btn btn-p", "icon-play", "Kita pamoka", `/pamokos/${lesson.nextId}`), retry, map);
            else
                actions.append(button("btn btn-p", "icon-lock", "Prisijunk ir tęsk", () => openLogin()), retry, map);
        } else {
            actions.append(retry, map);
        }
        els.overlay.classList.add("visible");
    }

    function icon(name) {
        return `<svg class="ui-icon" aria-hidden="true"><use href="#${name}"></use></svg>`;
    }
    function button(className, iconName, label, onClick) {
        const node = document.createElement("button");
        node.type = "button";
        node.className = className;
        node.innerHTML = icon(iconName) + label;
        node.addEventListener("click", onClick);
        return node;
    }
    function link(className, iconName, label, href) {
        const node = document.createElement("a");
        node.className = className;
        node.href = href;
        node.innerHTML = icon(iconName) + label;
        return node;
    }

    // ---------- pradžia ----------

    els.start.addEventListener("click", () => {
        newRun();
        els.intro.hidden = true;
        els.stage.hidden = false;
        els.wpm.textContent = "0";
        els.acc.textContent = "100";
        renderChips();
        fetchRound();
        els.start.blur();
    });

    document.addEventListener("keydown", onKeyDown);

    // klaviatūra piešiama, kai visi kintamieji jau paskelbti
    LessonKeyboard.render(els.keyboard);
    LessonKeyboard.attachHands(els.hands, els.caption);
    LessonKeyboard.markNew(lesson.newKeys);
    applyHints();
})();
