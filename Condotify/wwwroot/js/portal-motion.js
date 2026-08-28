(function () {
    "use strict";

    const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

    function markReady() {
        document.documentElement.classList.add("motion-ready");
    }

    async function exitById(id) {
        const element = document.getElementById(id);
        if (!element || reducedMotion.matches) return;

        const animation = element.animate([
            { opacity: 1, transform: "translateX(0)" },
            { opacity: 0, transform: "translateX(8px)" }
        ], {
            duration: 130,
            easing: "cubic-bezier(.4, 0, 1, 1)",
            fill: "forwards"
        });

        try {
            await animation.finished;
        } catch {
            // A renderizacao do Blazor pode substituir o elemento antes do fim.
        }
    }

    function emphasize(id) {
        const element = document.getElementById(id);
        if (!element || reducedMotion.matches) return;
        element.animate([
            { backgroundColor: "rgba(9, 37, 87, .08)" },
            { backgroundColor: "transparent" }
        ], { duration: 520, easing: "ease-out" });
    }

    window.portalMotion = {
        refresh: markReady,
        exitById,
        emphasize
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", markReady, { once: true });
    } else {
        markReady();
    }
})();
