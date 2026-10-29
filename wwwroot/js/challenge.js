// Mėnesio iššūkis (Views/Challenge/Index.cshtml): serveris duoda žodžius ir matuoja laiką, naršyklė tik rodo ir skaičiuoja klaidas
(function () {
    const $ = (id) => document.getElementById(id);
    const startBtn = $("challenge-start");
    if (!startBtn) return;
    const stage = $("challenge-stage"), countEl = $("challenge-count"), wordEl = $("challenge-word");
    const progressEl = $("challenge-progress"), input = $("challenge-input");
    const resultEl = $("challenge-result"), errorEl = $("challenge-error");

    let words = [], wi = 0, ci = 0, errors = 0, active = false;

    function fail(message) {
        errorEl.textContent = message;
        errorEl.hidden = false;
        startBtn.disabled = false;
        stage.hidden = true;
        active = false;
    }

    function renderWord() {
        wordEl.replaceChildren();
        [...words[wi]].forEach((ch, i) => {
            const span = document.createElement("span");
            span.textContent = ch;
            span.className = i < ci ? "is-done" : i === ci ? "is-current" : "";
            wordEl.append(span);
        });
        progressEl.textContent = `Žodis ${wi + 1} iš ${words.length} · klaidų: ${errors}`;
    }

    async function finish() {
        active = false;
        input.blur();
        try {
            const res = await fetch("/challenge/finish", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ errors })
            });
            if (!res.ok) throw new Error(res.status);
            const data = await res.json();
            stage.hidden = true;
            resultEl.replaceChildren();
            const line = document.createElement("p");
            line.className = "reward-gains";
            line.textContent = `${data.score.toFixed(2)} s (${data.seconds.toFixed(2)} s, klaidų: ${data.errors}) · vieta: ${data.rank}`;
            resultEl.append(line);
            const best = document.createElement("p");
            best.className = "reward-next";
            best.textContent = data.isBest ? "Naujas geriausias rezultatas!" : `Geriausias liko ${data.bestScore.toFixed(2)} s`;
            resultEl.append(best);
            for (const note of data.notes) {
                const p = document.createElement("p");
                p.className = "reward-levelup";
                p.textContent = note;
                resultEl.append(p);
            }
            resultEl.hidden = false;
            startBtn.disabled = false;
            startBtn.textContent = "Bandyti dar kartą";
        } catch {
            fail("Rezultato išsaugoti nepavyko (galbūt per greitai arba pasibaigė mėnuo). Bandyk dar kartą.");
        }
    }

    function press(ch) {
        if (!active) return;
        if (ch === words[wi][ci]) {
            ci++;
            if (ci === words[wi].length) {
                wi++;
                ci = 0;
                if (wi === words.length) { finish(); return; }
            }
        } else {
            errors++;
            wordEl.classList.remove("is-wrong");
            void wordEl.offsetWidth; // perkrauna animaciją
            wordEl.classList.add("is-wrong");
        }
        renderWord();
    }

    // Įvedimas tik per laukelį: įklijuoti ir kelių simbolių įterpimai ignoruojami
    input.addEventListener("paste", (e) => e.preventDefault());
    input.addEventListener("drop", (e) => e.preventDefault());
    input.addEventListener("input", () => {
        const value = input.value;
        input.value = "";
        if (value.length === 1) press(value);
    });
    stage.addEventListener("click", () => input.focus());

    startBtn.addEventListener("click", async () => {
        errorEl.hidden = true;
        resultEl.hidden = true;
        startBtn.disabled = true;
        try {
            const res = await fetch("/challenge/start", { method: "POST" });
            if (!res.ok) throw new Error(res.status);
            const data = await res.json();
            words = data.words;
            wi = 0; ci = 0; errors = 0; active = false;
            stage.hidden = false;
            wordEl.replaceChildren();
            progressEl.textContent = "";
            input.focus();
            for (let left = data.countdown; left > 0; left--) {
                countEl.textContent = String(left);
                await new Promise((r) => setTimeout(r, 1000));
            }
            countEl.textContent = "Rašyk!";
            active = true;
            input.focus();
            renderWord();
        } catch {
            fail("Nepavyko pradėti. Patikrink, ar esi prisijungęs.");
        }
    });
})();
