// Lenktynių perklausa: MultiPlay (po lenktynių) ir /race/replay/{resultId}.
// Reikia _Replay.cshtml markup ir progressEscape (progress.js).

let replayTimeline = null;
let replayTextLength = 0;
let replayText = null;
let replayTimer = null;
let replaySpeed = 1;
let replayIndex = 0;

// level (nebūtinas) rodomas kaip ženkliukas, toks pat kaip _Monkey.cshtml
// look – žaidėjo PlayerCard (hatColor, furColor iš parduotuvės); null svečiams ir botams
function monkeyTemplate(colorIndex, level, look) {
    const templates = document.querySelectorAll(
        "#replay-monkey-templates .monkey-svg");
    const template = templates[Math.abs(colorIndex || 0) % templates.length];
    if (!template) return "";
    if (level == null && !look) return template.outerHTML;
    const svg = template.cloneNode(true);
    if (look) {
        svg.querySelectorAll(".monkey-fur").forEach(part => part.setAttribute(
            part.getAttribute("fill") === "none" ? "stroke" : "fill", look.furColor));
        svg.querySelectorAll(".monkey-hat").forEach(part => part.setAttribute("fill", look.hatColor));
    }
    if (level != null)
        svg.insertAdjacentHTML("beforeend",
            `<g class="monkey-level"><circle cx="134" cy="134" r="24"/>` +
            `<text x="134" y="144" text-anchor="middle">${Number(level)}</text></g>`);
    return svg.outerHTML;
}

// spalva sutampa su beždžionės kepure iš #replay-monkey-templates (race.css .replay-color-*)
const replayColor = (player, index) => Math.abs(player.colorIndex ?? index) % 4;

// timeline = { tickMs, players: [{ id, name, colorIndex, series }] }; badge(player) – HTML po vardu
// text (nebūtinas) – lenktynių tekstas; rodomas tik jei ilgis sutampa su series reikšmėmis
function renderReplay(timeline, textLength, badge, text) {
    replayTimeline = timeline;
    replayTextLength = textLength;
    replayText = text && text.length === textLength ? text : null;
    document.getElementById("replay-text").hidden = !replayText;
    document.getElementById("replay-race").hidden = false;
    const track = document.getElementById("replay-track");
    track.innerHTML = timeline.players.map((player, index) => `
        <div class="replay-lane" data-replay-player="${progressEscape(player.id)}">
            <span class="replay-player-name">
                <span class="replay-dot replay-color-${replayColor(player, index)}" aria-hidden="true"></span>
                ${progressEscape(player.name)} ${badge(player)}
            </span>
            <div class="replay-lane-track" data-replay-track="${progressEscape(player.id)}">
                <div class="replay-monkey" data-replay-monkey="${progressEscape(player.id)}">
                ${monkeyTemplate(player.colorIndex ?? index)}
                </div>
            </div>
        </div>`).join("");
    const range = document.getElementById("replay-range");
    range.max = Math.max(0, (timeline.players[0]?.series.length || 1) - 1);
    range.value = 0;
    replayIndex = 0;
    updateReplayFrame();
}

function replayValue(player) {
    const series = player.series;
    return Math.min(replayTextLength, series[Math.min(replayIndex, series.length - 1)] || 0);
}

function renderReplayText() {
    const players = replayTimeline.players.map((player, index) => ({
        player, color: replayColor(player, index), value: replayValue(player) }));
    const leader = players.reduce((best, item) => item.value > best.value ? item : best);
    const caretsAt = new Map();
    players.forEach(item => caretsAt.set(item.value, [...(caretsAt.get(item.value) || []), item]));
    let html = `<mark class="replay-typed replay-color-${leader.color}">`;
    for (let i = 0; i <= replayText.length; i++) {
        if (i === leader.value) html += "</mark>";
        (caretsAt.get(i) || []).forEach(item => html +=
            `<span class="replay-caret replay-color-${item.color}" title="${progressEscape(item.player.name)}" aria-hidden="true"></span>`);
        if (i < replayText.length) html += progressEscape(replayText[i]);
    }
    document.getElementById("replay-text").innerHTML = html;
}

function updateReplayFrame() {
    if (!replayTimeline || !replayTimeline.players.length) return;
    if (replayText) renderReplayText();
    replayTimeline.players.forEach(player => {
        const percent = replayTextLength ? replayValue(player) / replayTextLength : 0;
        const monkey = document.querySelector(
            `[data-replay-monkey="${CSS.escape(player.id)}"]`);
        const track = document.querySelector(
            `[data-replay-track="${CSS.escape(player.id)}"]`);
        if (monkey && track) {
            const travel = Math.max(0, track.clientWidth - monkey.offsetWidth);
            monkey.style.transform = `translateX(${percent * travel}px)`;
        }
    });
    const totalTicks = replayTimeline.players[0]?.series.length || 1;
    const currentSeconds = replayIndex * (replayTimeline.tickMs || 250) / 1000;
    const totalSeconds = Math.max(0, (totalTicks - 1) *
        (replayTimeline.tickMs || 250) / 1000);
    document.getElementById("replay-range").value = replayIndex;
    document.getElementById("replay-time").textContent =
        `${currentSeconds.toFixed(1).replace(".", ",")} s / ` +
        `${totalSeconds.toFixed(1).replace(".", ",")} s`;
}

function setReplayPlayingState(isPlaying) {
    const button = document.getElementById("replay-play");
    button.setAttribute("aria-pressed", String(isPlaying));
    button.setAttribute("aria-label",
        isPlaying ? "Paleisti lenktynių replay iš naujo" :
            "Paleisti lenktynių replay");
    button.innerHTML = isPlaying
        ? `<svg class="control-icon" viewBox="0 0 24 24" aria-hidden="true">
             <path d="M7 5h4v14H7zm6 0h4v14h-4z"/></svg><span>Vyksta</span>`
        : `<svg class="control-icon" viewBox="0 0 24 24" aria-hidden="true">
             <path d="M8 5v14l11-7z"/></svg><span>Paleisti</span>`;
}

document.getElementById("replay-play").addEventListener("click", () => {
    if (replayTimer || !replayTimeline) return;
    setReplayPlayingState(true);
    replayTimer = setInterval(() => {
        const length = replayTimeline.players[0]?.series.length || 0;
        if (replayIndex >= length - 1) {
            clearInterval(replayTimer);
            replayTimer = null;
            setReplayPlayingState(false);
            return;
        }
        replayIndex++;
        updateReplayFrame();
    }, Math.max(16, 250 / replaySpeed));
});
document.getElementById("replay-pause").addEventListener("click", () => {
    clearInterval(replayTimer);
    replayTimer = null;
    setReplayPlayingState(false);
});
document.getElementById("replay-reset").addEventListener("click", () => {
    clearInterval(replayTimer);
    replayTimer = null;
    setReplayPlayingState(false);
    replayIndex = 0;
    updateReplayFrame();
});
document.querySelectorAll("[data-replay-speed]").forEach(button => {
    button.addEventListener("click", () => {
        replaySpeed = Number(button.dataset.replaySpeed);
        document.querySelectorAll("[data-replay-speed]").forEach(speedButton =>
            speedButton.setAttribute("aria-pressed", speedButton === button ? "true" : "false"));
        if (replayTimer) {
            clearInterval(replayTimer);
            replayTimer = null;
            document.getElementById("replay-play").click();
        }
    });
});
document.getElementById("replay-range").addEventListener("input", event => {
    clearInterval(replayTimer);
    replayTimer = null;
    setReplayPlayingState(false);
    replayIndex = Number(event.target.value);
    updateReplayFrame();
});
window.addEventListener("resize", () => {
    if (replayTimeline) updateReplayFrame();
});
