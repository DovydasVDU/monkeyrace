// Žaidėjų paieška profilio puslapyje: GET /api/users/search?q= (bent 3 raidės, daugiausia 8 rezultatai)
(function () {
    const input = document.getElementById("player-search");
    const list = document.getElementById("player-results");
    if (!input || !list) return;

    let timer = 0;
    let latest = 0;

    function show(hits, message) {
        list.replaceChildren();
        if (message) {
            const li = document.createElement("li");
            li.className = "shop-note";
            li.textContent = message;
            list.append(li);
            return;
        }
        for (const hit of hits) {
            const li = document.createElement("li");
            const link = document.createElement("a");
            link.href = "/u/" + encodeURIComponent(hit.username);
            link.textContent = hit.username;
            const meta = document.createElement("span");
            meta.textContent = ` · ${hit.level} lygis, ${hit.title}`;
            li.append(link, meta);
            list.append(li);
        }
    }

    input.addEventListener("input", () => {
        clearTimeout(timer);
        const query = input.value.trim();
        if (query.length < 3) { show([]); return; }
        timer = setTimeout(async () => {
            const id = ++latest;
            try {
                const res = await fetch("/api/users/search?q=" + encodeURIComponent(query));
                if (!res.ok) throw new Error(res.status);
                const hits = await res.json();
                if (id === latest) show(hits, hits.length ? "" : "Nieko nerasta.");
            } catch {
                if (id === latest) show([], "Paieška nepavyko, bandyk vėliau.");
            }
        }, 300);
    });
})();
