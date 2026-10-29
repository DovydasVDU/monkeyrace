// Bendri XP, lygių ir bananų rodymo pagalbininkai (solo, multiplayer).
// Serveris viską paskaičiuoja (Progression.cs), čia tik atvaizduojama.

function progressEscape(value) {
    return String(value).replace(/[&<>"']/g, char =>
        ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]);
}

function progressIcon(name) {
    return `<svg class="icon icon-${name}" aria-hidden="true"><use href="#icon-${name}"></use></svg>`;
}

function progressPercent(value) {
    return `${Math.round(Math.max(0, Math.min(1, value || 0)) * 100)}%`;
}

// Botų lygis pagal greitį (WPM/8, 1-10), kad trasa atrodytų vientisai
function botLevel(wpm) {
    return Math.max(1, Math.min(10, Math.round((wpm || 0) / 8)));
}

function prefersReducedMotion() {
    return window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

// Rezultatų ekrano apdovanojimas. reward = RaceReward iš serverio, null = rezultatas neįskaitytas
function renderReward(container, reward) {
    if (!container) return;
    container.hidden = false;
    container.classList.add("reward-panel");

    if (!reward) {
        container.innerHTML = `<p class="reward-muted">Šis bandymas neįskaitytas.</p>`;
        return;
    }

    if (!reward.saved) {
        container.innerHTML = `
            <p class="reward-guest">Būtum gavęs <strong>+${reward.xpGained} XP</strong>:
                <button type="button" class="reward-login" onclick="openLogin()">prisijunk!</button></p>`;
        return;
    }

    const next = reward.xpToNext == null
        ? "Maksimalus lygis"
        : `Iki ${reward.level + 1} lygio: ${reward.xpToNext} XP`;
    container.innerHTML = `
        ${reward.firstRace
            ? `<p class="reward-first">${progressIcon("medal")} Sveikiname su pirmomis lenktynėmis! Gavai pradžios dovaną.</p>`
            : ""}
        <p class="reward-gains">
            <strong>+${reward.xpGained} XP</strong>
            <span aria-hidden="true">·</span>
            <strong>+${reward.bananasGained}</strong> ${progressIcon("banana")}
            <span class="visually-hidden">bananų</span>
        </p>
        <div class="reward-level">
            <span class="level-badge" title="Lygis ${reward.level}">${reward.level}</span>
            <div class="xp-track reward-xp" role="progressbar" aria-label="XP iki kito lygio"
                 aria-valuemin="0" aria-valuemax="100"
                 aria-valuenow="${Math.round((reward.levelProgress || 0) * 100)}">
                <span style="width:${progressPercent(reward.leveledUp ? 0 : reward.levelProgressBefore)}"></span>
            </div>
        </div>
        <p class="reward-next">${next} · ${progressIcon("banana")} ${reward.bananas}</p>
        ${reward.leveledUp
            ? `<p class="reward-levelup" role="status">${progressIcon("star")} Naujas lygis! Dabar esi ${reward.level} lygio.</p>`
            : ""}`;

    // XP juosta užsipildo nuo ankstesnės padėties iki naujos
    const bar = container.querySelector(".reward-xp span");
    if (prefersReducedMotion()) {
        bar.style.width = progressPercent(reward.levelProgress);
        return;
    }
    requestAnimationFrame(() => requestAnimationFrame(() => {
        bar.style.width = progressPercent(reward.levelProgress);
    }));
}

// Žaidėjo kortelė užvedus ant trasos. player = RacePlayerState
function playerCardHtml(player) {
    const name = `<strong>${progressEscape(player.username)}</strong>`;
    if (player.isBot) {
        return `<div class="pc-head">${name}<span>Lygis ${botLevel(player.botWpm)}</span></div>
            <p>Botas · ~${player.botWpm || "?"} WPM</p>`;
    }
    const card = player.card;
    if (!card) {
        return `<div class="pc-head">${name}</div><p>Svečias: prisijunk, kad kauptum XP</p>`;
    }
    // titulas viršuje, kiekvienas duomuo savo eilutėje; titulo dar gali nebūti (card.title)
    const title = card.title ? `<div class="pc-title">${progressEscape(card.title)}</div>` : "";
    return `${title}<div class="pc-head">${name}<span>Lygis ${card.level}</span></div>
        <div class="xp-track"><span style="width:${progressPercent(card.levelProgress)}"></span></div>
        <dl class="pc-rows">
            <div><dt>Geriausias WPM</dt><dd>${card.bestWpm}</dd></div>
            <div><dt>Vidutinis WPM</dt><dd>${card.averageWpm}</dd></div>
            <div><dt>Tikslumas</dt><dd>${Number(card.averageAccuracy).toFixed(1)}%</dd></div>
        </dl>`;
}

// Lygis, rodomas ant beždžionėlės (null svečiui)
function playerLevel(player) {
    if (player.isBot) return botLevel(player.botWpm);
    return player.card ? player.card.level : null;
}
