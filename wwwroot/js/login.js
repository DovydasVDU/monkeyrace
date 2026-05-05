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
async function login() {

    const username = document.getElementById("login-user").value
    const password = document.getElementById("login-pass").value
    // kreipiamės į AuthController Login metodą POST užklausa, siunčiame username ir password formatu application/x-www-form-urlencoded
    const res = await fetch("/Auth/Login", {
        method: "POST",
        headers: {
            "Content-Type": "application/x-www-form-urlencoded"
        },
        body: `username=${encodeURIComponent(username)}&password=${encodeURIComponent(password)}`

    })
    // AuthController grąžiną 200 OK jei login sėkmingas, kitu atveju 400 Bad Request su klaidos pranešimu

    if (res.ok)
        location.reload()
    else {

        let message = "login failed"

        try {
            const tempText = await res.text();
            try {
                const data = JSON.parse(tempText);
                message = data.message || message;
            } catch {
                message = tempText || message;
            }
        } catch (e) {
            console.error("Error reading response:", e);
        }

        alert(message)
    }

}
async function register() {

    const username = document.getElementById("login-user").value
    const password = document.getElementById("login-pass").value

    if (!username || !password) {
        alert("enter username and password")
        return
    }
    // kreipiamės į AuthController Register metodą POST užklausa, siunčiame username ir password formatu application/x-www-form-urlencoded
    const res = await fetch("/Auth/Register", {
        method: "POST",
        headers: {
            "Content-Type": "application/x-www-form-urlencoded"
        },
        body: `username=${encodeURIComponent(username)}&password=${encodeURIComponent(password)}`

    })
    // AuthController grąžiną 200 OK jei registracija sėkminga, kitu atveju 400 Bad Request su klaidos pranešimu
    if (res.ok) {
        alert("account created")
        location.reload()
    }
    else {


        let message = "register failed"

        try {
            const tempText = await res.text();
            try {
                const data = JSON.parse(tempText);
                message = data.message || message;
            } catch {
                message = tempText || message;
            }
        } catch (e) {
            console.error("Error reading response:", e);
        }

        alert(message)
    }
}

// NAVBAR modalas
function toggleMenu(){
        document.getElementById("userDropdown").classList.toggle("visible");
    }