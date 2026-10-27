// Pamokų klaviatūra ir rankos: piešia klaviatūrą, rodo kurį klavišą spausti ir kokiu pirštu.
// Klavišų vietos kaip US klaviatūroje, lietuviškos raidės – skaičių eilutėje (ą č ę ė į š ų ū ... ž).
// Jei tavo išdėstymas kitoks, keisk tik MR_LAYOUT. k – klavišo ženklas US klaviatūroje, lt – lietuviška raidė tame klavise,
// f – pirštas: pirmoji raidė l/r (ranka), antra p/r/m/i (mažasis, bevardis, vidurinis, rodomasis).
const MR_LAYOUT = {
    rows: [
        [{ k: "`", f: "lp" }, { k: "1", lt: "ą", f: "lp" }, { k: "2", lt: "č", f: "lr" }, { k: "3", lt: "ę", f: "lm" },
            { k: "4", lt: "ė", f: "li" }, { k: "5", lt: "į", f: "li" }, { k: "6", lt: "š", f: "ri" }, { k: "7", lt: "ų", f: "ri" },
            { k: "8", lt: "ū", f: "rm" }, { k: "9", f: "rr" }, { k: "0", f: "rp" }, { k: "-", f: "rp" }, { k: "=", lt: "ž", f: "rp" }],
        [{ k: "q", f: "lp" }, { k: "w", f: "lr" }, { k: "e", f: "lm" }, { k: "r", f: "li" }, { k: "t", f: "li" },
            { k: "y", f: "ri" }, { k: "u", f: "ri" }, { k: "i", f: "rm" }, { k: "o", f: "rr" }, { k: "p", f: "rp" },
            { k: "[", f: "rp" }, { k: "]", f: "rp" }],
        [{ k: "a", f: "lp" }, { k: "s", f: "lr" }, { k: "d", f: "lm" }, { k: "f", f: "li", home: true }, { k: "g", f: "li" },
            { k: "h", f: "ri" }, { k: "j", f: "ri", home: true }, { k: "k", f: "rm" }, { k: "l", f: "rr" }, { k: ";", f: "rp" },
            { k: "'", f: "rp" }],
        [{ k: "z", f: "lp" }, { k: "x", f: "lr" }, { k: "c", f: "lm" }, { k: "v", f: "li" }, { k: "b", f: "li" },
            { k: "n", f: "ri" }, { k: "m", f: "ri" }, { k: ",", f: "rm" }, { k: ".", f: "rr" }, { k: "/", f: "rp" }]
    ],
    // ženklai, rašomi su Shift kitame klavise: "?" = Shift + "/"
    shifted: { "?": "/" },
    shiftFingers: { left: "lp", right: "rp" },
    spaceFinger: "rt"
};

const MR_FINGER_NAMES = {
    lp: "kairės rankos mažuoju pirštu",
    lr: "kairės rankos bevardžiu",
    lm: "kairės rankos viduriniu pirštu",
    li: "kairės rankos rodomuoju pirštu",
    lt: "nykščiu",
    rt: "nykščiu",
    ri: "dešinės rankos rodomuoju pirštu",
    rm: "dešinės rankos viduriniu pirštu",
    rr: "dešinės rankos bevardžiu",
    rp: "dešinės rankos mažuoju pirštu"
};

const LessonKeyboard = (() => {
    let root = null;
    const keyEls = new Map();   // ženklas (US arba lt) -> elementas
    let shiftEls = {};          // left / right
    let spaceEl = null;
    let handsRoot = null;
    let captionEl = null;
    let lastTargets = [];

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text != null) node.textContent = text;
        return node;
    }

    function makeKey(def) {
        const key = el("div", "kb-key");
        key.dataset.finger = def.f;
        if (def.home) key.classList.add("kb-home");
        if (def.lt) {
            key.classList.add("kb-lt");
            key.appendChild(el("span", "kb-label", def.lt.toUpperCase()));
            key.appendChild(el("span", "kb-sub", def.k));
        } else {
            key.appendChild(el("span", "kb-label", def.k.toUpperCase()));
        }
        key.dataset.key = def.lt || def.k;
        keyEls.set(def.k, key);
        if (def.lt) keyEls.set(def.lt, key);
        return key;
    }

    function render(container) {
        root = container;
        root.textContent = "";
        keyEls.clear();
        const board = el("div", "kb");
        MR_LAYOUT.rows.forEach((row, index) => {
            const rowEl = el("div", `kb-row kb-row-${index}`);
            if (index === 3) {
                shiftEls.left = el("div", "kb-key kb-shift", "Shift");
                shiftEls.left.dataset.finger = MR_LAYOUT.shiftFingers.left;
                rowEl.appendChild(shiftEls.left);
            }
            row.forEach(def => rowEl.appendChild(makeKey(def)));
            if (index === 3) {
                shiftEls.right = el("div", "kb-key kb-shift", "Shift");
                shiftEls.right.dataset.finger = MR_LAYOUT.shiftFingers.right;
                rowEl.appendChild(shiftEls.right);
            }
            board.appendChild(rowEl);
        });
        const spaceRow = el("div", "kb-row kb-row-4");
        spaceEl = el("div", "kb-key kb-space", "");
        spaceEl.dataset.finger = MR_LAYOUT.spaceFinger;
        spaceRow.appendChild(spaceEl);
        board.appendChild(spaceRow);
        root.appendChild(board);
    }

    function attachHands(svgRoot, caption) {
        handsRoot = svgRoot;
        captionEl = caption;
    }

    // Kur yra simbolis: klavišo elementas, ar reikia Shift, kuriuo pirštu ir kuria Shift ranka
    function locate(ch) {
        if (ch === " ")
            return { el: spaceEl, finger: MR_LAYOUT.spaceFinger, shift: false, space: true };
        const lower = ch.toLowerCase();
        const mapped = MR_LAYOUT.shifted[ch];
        const baseKey = mapped || lower;
        const keyEl = keyEls.get(baseKey);
        if (!keyEl) return null;
        const finger = keyEl.dataset.finger;
        const shift = !!mapped || ch !== lower;
        const side = finger[0] === "l" ? "right" : "left"; // Shift spaudžiamas priešinga ranka
        return {
            el: keyEl, finger, shift,
            shiftEl: shiftEls[side], shiftFinger: MR_LAYOUT.shiftFingers[side]
        };
    }

    function clearTargets() {
        lastTargets.forEach(node => node.classList.remove("is-target"));
        lastTargets = [];
        if (handsRoot)
            handsRoot.querySelectorAll(".is-active").forEach(node => node.classList.remove("is-active"));
        if (captionEl) captionEl.textContent = "";
    }

    function setHandActive(finger) {
        if (!handsRoot) return;
        const node = handsRoot.querySelector(`[data-finger="${finger}"]`);
        if (node) node.classList.add("is-active");
    }

    // Parodo, ką spausti dabar
    function highlight(ch) {
        clearTargets();
        const spot = locate(ch);
        if (!spot) return;
        spot.el.classList.add("is-target");
        lastTargets.push(spot.el);
        setHandActive(spot.finger);
        let text = spot.space
            ? "Tarpą spausk nykščiu."
            : `Spausk ${MR_FINGER_NAMES[spot.finger]}.`;
        if (spot.shift && spot.shiftEl) {
            spot.shiftEl.classList.add("is-target");
            lastTargets.push(spot.shiftEl);
            setHandActive(spot.shiftFinger);
            text = `Laikyk Shift ${spot.shiftFinger[0] === "l" ? "kairės" : "dešinės"} rankos mažuoju pirštu ir spausk ${MR_FINGER_NAMES[spot.finger]}.`;
        }
        if (captionEl) captionEl.textContent = text;
    }

    // Trumpas pagavimas: ok – teisingas paspaudimas, miss – ne tas klavišas
    function flash(ch, kind) {
        const spot = ch && ch.length === 1 ? locate(ch) : null;
        if (!spot || !spot.el) return;
        const cls = kind === "ok" ? "is-hit" : "is-miss";
        spot.el.classList.remove("is-hit", "is-miss");
        void spot.el.offsetWidth;
        spot.el.classList.add(cls);
        setTimeout(() => spot.el.classList.remove(cls), 220);
    }

    // Naujai mokomi klavišai pažymimi visą pamoką
    function markNew(chars) {
        keyEls.forEach(node => node.classList.remove("is-new"));
        Object.values(shiftEls).forEach(node => node.classList.remove("is-new"));
        for (const ch of chars) {
            if (ch === "⇧") {
                Object.values(shiftEls).forEach(node => node.classList.add("is-new"));
                continue;
            }
            const spot = locate(ch);
            if (spot && spot.el) spot.el.classList.add("is-new");
            if (ch in MR_LAYOUT.shifted && spot && spot.shiftEl) spot.shiftEl.classList.add("is-new");
        }
    }

    // Ar paspaustas ženklas yra to paties klavišo skaičius (lietuviškos raidės vietoje) – tikėtina, kad įjungtas ne lietuviškas išdėstymas
    function looksLikeWrongLayout(expected, typed) {
        const spot = locate(expected);
        return !!(spot && spot.el && !spot.space && /[0-9`\-=]/.test(typed) &&
            MR_LAYOUT.rows[0].some(def => def.lt === expected.toLowerCase() && def.k === typed));
    }

    return { render, attachHands, highlight, clearTargets, flash, markNew, locate, looksLikeWrongLayout };
})();
