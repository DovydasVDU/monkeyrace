/* login */
function openLogin() {
    const modal = document.getElementById("loginModal");
    modal.classList.add("visible");

    // Add event listener to close modal when clicking outside of it
    modal.addEventListener("click", function(event) {
        if (event.target === modal) {
            closeLogin();
        }
    });
}

function closeLogin() {
    document.getElementById("loginModal").classList.remove("visible");
}
// klaida rodoma modalo apačioje, ne alert(); null – paslepia
function showLoginMessage(text) {
    const box = document.getElementById("login-message");
    document.getElementById("login-message-text").textContent = text || "";
    box.hidden = !text;
}

// bendras Login/Register: AuthController grąžina 200 OK arba klaidą su { message }
async function sendAuth(url, fallbackMessage) {
    const username = document.getElementById("login-user").value.trim()
    const password = document.getElementById("login-pass").value

    if (!username || !password) {
        showLoginMessage("Įvesk slapyvardį ir slaptažodį.")
        return
    }
    showLoginMessage(null)

    let res
    try {
        res = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body: `username=${encodeURIComponent(username)}&password=${encodeURIComponent(password)}`
        })
    } catch {
        showLoginMessage("Nepavyko susisiekti su serveriu. Patikrink ryšį.")
        return
    }

    if (res.ok) {
        location.reload()
        return
    }

    let message = fallbackMessage
    try {
        message = (await res.json()).message || message
    } catch { /* tuščias atsakymas – lieka fallbackMessage */ }
    showLoginMessage(message)
}

function login() {
    return sendAuth("/Auth/Login", "Nepavyko prisijungti.")
}

function register() {
    return sendAuth("/Auth/Register", "Nepavyko užsiregistruoti.")
}

// NAVBAR modalas
function openMenu() {
    document.getElementById("userDropdown").classList.add("visible");
}

function closeMenu() {
    document.getElementById("userDropdown").classList.remove("visible");
}


