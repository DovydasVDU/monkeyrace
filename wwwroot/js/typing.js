// Bendros solo (Play) ir online (MultiPlay) spausdinimo funkcijos

// Laukiamo žodžio simboliai: correct / wrong / pending, po žodžio – iki 8 "extra"
function renderWord(expectedWord, typedWord) {
    const result = [];
    for (let index = 0; index < expectedWord.length; index++) {
        const typed = typedWord[index];
        result.push({
            ch: typed || expectedWord[index],
            expected: expectedWord[index],
            state: typed
                ? typed === expectedWord[index] ? "correct" : "wrong"
                : "pending"
        });
    }
    for (let index = expectedWord.length;
         index < Math.min(typedWord.length, expectedWord.length + 8);
         index++) {
        result.push({ ch: typedWord[index], expected: "", state: "extra" });
    }
    return result;
}

// ta pati formulė kaip serveryje (MatchmakingService): simboliai / 5 per minutę
function typingWpm(chars, elapsedMs) {
    return elapsedMs > 0 ? Math.round(chars / 5 / (elapsedMs / 60000)) : 0;
}

function typingAccuracy(totalKeystrokes, wrongKeystrokes) {
    return totalKeystrokes === 0
        ? 100
        : Math.max(0, (totalKeystrokes - wrongKeystrokes) * 100 / totalKeystrokes);
}

// Klaidų sekimas profilio statistikai: kas paspausta vietoj laukiamo simbolio ir kurie žodžiai buvo su klaidomis.
// Po lenktynių send() vieną kartą išsiunčia į POST /race/mistakes (svečiui serveris grąžina 401 – nieko nedarome).
function createMistakeTracker() {
    const keys = new Map();
    const words = [];
    let sent = false;
    return {
        key(expected, typed) {
            if (!expected || !typed) return;
            const id = expected + "\u0000" + typed;
            keys.set(id, (keys.get(id) || 0) + 1);
        },
        word(word, hadError) {
            words.push({ word, hadError: !!hadError });
        },
        send() {
            if (sent || (keys.size === 0 && words.length === 0)) return;
            sent = true;
            fetch("/race/mistakes", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    keys: [...keys].map(([id, count]) => {
                        const [expected, typed] = id.split("\u0000");
                        return { expected, typed, count };
                    }),
                    words
                })
            }).catch(() => {});
        }
    };
}
