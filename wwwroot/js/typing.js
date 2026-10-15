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
