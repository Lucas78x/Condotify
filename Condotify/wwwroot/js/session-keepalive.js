(() => {
    "use strict";

    const marker = document.getElementById("condotify-authenticated-session");
    if (!marker) {
        window.condotifySessionReady = Promise.resolve(true);
        return;
    }

    const endpoint = "/Login/KeepAlive";
    const minimumDelayMs = 60_000;
    const retryDelayMs = 90_000;
    const refreshLeadSeconds = 5 * 60;
    let timerId = 0;
    let requestInFlight = false;
    let expiresAtMs = 0;
    let unauthorizedRecovery = null;

    function currentReturnUrl() {
        return `${window.location.pathname}${window.location.search}${window.location.hash}`;
    }

    function goToLogin() {
        window.location.replace(`/Login?ReturnUrl=${encodeURIComponent(currentReturnUrl())}`);
    }

    async function recoverUnauthorizedSession() {
        let navigating = false;
        try {
            const response = await fetch(`${endpoint}?force=true`, {
                method: "GET",
                credentials: "same-origin",
                cache: "no-store",
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });

            if (response.status === 401) {
                navigating = true;
                goToLogin();
                return false;
            }

            if (!response.ok) return false;

            // Mesmo quando outra aba realizou a rotacao, o cookie desta resposta
            // contem o principal atual. Um reload e obrigatorio para o circuito
            // Blazor deixar de usar os claims capturados na conexao anterior.
            navigating = true;
            window.location.reload();
            return true;
        } catch {
            return false;
        } finally {
            if (!navigating) unauthorizedRecovery = null;
        }
    }

    window.condotifySession = window.condotifySession || {};
    window.condotifySession.handleUnauthorized = function () {
        unauthorizedRecovery ??= recoverUnauthorizedSession();
        return unauthorizedRecovery;
    };

    function schedule(seconds) {
        window.clearTimeout(timerId);
        const delay = Math.max(minimumDelayMs, seconds * 1000);
        timerId = window.setTimeout(() => void maintainSession(), delay);
    }

    async function maintainSession() {
        if (requestInFlight) return true;
        if (document.visibilityState === "hidden") {
            schedule(60);
            return true;
        }

        requestInFlight = true;
        try {
            const response = await fetch(endpoint, {
                method: "GET",
                credentials: "same-origin",
                cache: "no-store",
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });

            if (response.status === 401) {
                goToLogin();
                return false;
            }

            if (!response.ok) {
                schedule(retryDelayMs / 1000);
                return true;
            }

            const session = await response.json();
            if (session.refreshed) {
                window.location.reload();
                return false;
            }

            const remaining = Number(session.expiresInSeconds);
            if (Number.isFinite(remaining))
                expiresAtMs = Date.now() + remaining * 1000;

            schedule(Number.isFinite(remaining)
                ? Math.max(60, remaining - refreshLeadSeconds)
                : retryDelayMs / 1000);
            return true;
        } catch {
            schedule(retryDelayMs / 1000);
            return true;
        } finally {
            requestInFlight = false;
        }
    }

    document.addEventListener("visibilitychange", () => {
        if (document.visibilityState !== "visible") return;

        // Depois de uma aba suspensa, um reload passa primeiro pela verificacao
        // inicial e impede que o circuito Blazor use um access token vencido.
        if (expiresAtMs > 0 && expiresAtMs - Date.now() <= refreshLeadSeconds * 1000) {
            window.location.reload();
            return;
        }

        void maintainSession();
    });

    window.condotifySessionReady = maintainSession();
})();
