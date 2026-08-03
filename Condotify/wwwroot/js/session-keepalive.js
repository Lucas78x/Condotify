(() => {
    "use strict";

    if (!document.getElementById("condotify-authenticated-session"))
        return;

    const endpoint = "/Login/KeepAlive";
    const minimumDelayMs = 60_000;
    const retryDelayMs = 90_000;
    const refreshLeadSeconds = 5 * 60;
    let timerId = 0;
    let requestInFlight = false;

    function schedule(seconds) {
        window.clearTimeout(timerId);
        const delay = Math.max(minimumDelayMs, seconds * 1000);
        timerId = window.setTimeout(maintainSession, delay);
    }

    async function maintainSession() {
        if (requestInFlight || document.visibilityState === "hidden") {
            schedule(60);
            return;
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
                const returnUrl = `${window.location.pathname}${window.location.search}${window.location.hash}`;
                window.location.assign(`/Login?ReturnUrl=${encodeURIComponent(returnUrl)}`);
                return;
            }

            if (!response.ok) {
                schedule(retryDelayMs / 1000);
                return;
            }

            const session = await response.json();
            if (session.refreshed) {
                window.location.reload();
                return;
            }

            const remaining = Number(session.expiresInSeconds);
            schedule(Number.isFinite(remaining)
                ? Math.max(60, remaining - refreshLeadSeconds)
                : retryDelayMs / 1000);
        } catch {
            schedule(retryDelayMs / 1000);
        } finally {
            requestInFlight = false;
        }
    }

    document.addEventListener("visibilitychange", () => {
        if (document.visibilityState === "visible")
            void maintainSession();
    });

    void maintainSession();
})();
