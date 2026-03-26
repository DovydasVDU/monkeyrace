// wwwroot/js/race.js
class TypingRace {
    constructor(targetText) {
        this.targetText = targetText;
        this.startTime = null;
        this.errors = 0;
        this.isFinished = false;
    }

    start() {
        this.startTime = Date.now();
    }

    // Kviečiama kiekvienam paspaudimui
    onInput(currentInput) {
        if (!this.startTime) this.start();
        
        const elapsed = (Date.now() - this.startTime) / 1000 / 60; // minutėmis
        const wordsTyped = currentInput.length / 5;  // Standartinis WPM skaičiavimas
        
        const wpm = Math.round(wordsTyped / elapsed);
        const accuracy = this.calculateAccuracy(currentInput);
        
        // Atnaujiname UI
        document.getElementById('wpm-display').textContent = wpm;
        document.getElementById('accuracy-display').textContent = accuracy + '%';
        
        // Tikriname ar baigta
        if (currentInput === this.targetText) {
            this.finish(wpm, accuracy);
        }
        
        return { wpm, accuracy };
    }

    calculateAccuracy(currentInput) {
        if (currentInput.length === 0) return 100;
        
        let correct = 0;
        for (let i = 0; i < currentInput.length; i++) {
            if (currentInput[i] === this.targetText[i]) correct++;
        }
        return Math.round((correct / currentInput.length) * 100);
    }

    finish(wpm, accuracy) {
        this.isFinished = true;
        const timeSeconds = Math.round((Date.now() - this.startTime) / 1000);
        
        // Siunčiame rezultatus į serverį
        fetch('/Race/SaveResult', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                wpm: wpm,
                accuracy: accuracy,
                timeSeconds: timeSeconds,
                raceId: window.currentRaceId
            })
        }).then(r => r.json())
          .then(data => showResults(data));
    }
}