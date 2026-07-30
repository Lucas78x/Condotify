(function () {
    "use strict";

    const motionQuery = window.matchMedia("(prefers-reduced-motion: reduce)");
    const animated = new WeakSet();
    const delays = new WeakMap();
    const pendingRoots = new Set();
    const rowSelector = [
        ".person-row",
        ".credential-row",
        ".access-route-row",
        ".access-user-row",
        ".operational-alert-item",
        ".notification-delivery-row",
        ".operation-batch-row",
        ".mud-table-body .mud-table-row"
    ].join(",");

    const hardwareConcurrency = navigator.hardwareConcurrency || 8;
    const deviceMemory = navigator.deviceMemory || 8;
    const constrainedDevice = hardwareConcurrency <= 4 || deviceMemory <= 4;
    const maxAnimatedSiblings = constrainedDevice ? 4 : 8;
    const staggerStep = constrainedDevice ? 0 : 14;
    const siblingLimit = `:nth-child(-n+${maxAnimatedSiblings})`;
    const repeatedSelector = [
        ".stats-grid > *",
        ".module-kpis > *",
        ".dashboard-pulse-strip > *",
        ".structure-overview-strip > *",
        ".unit-detail-kpis > *",
        ".hierarchy-card",
        ".device-card",
        ".person-row",
        ".credential-row",
        ".access-route-row",
        ".access-user-row",
        ".operational-alert-item",
        ".notification-delivery-row",
        ".operation-batch-row",
        ".mud-table-body .mud-table-row"
    ].map(value => `${value}${siblingLimit}`).join(",");
    const selector = [
        ".page-header",
        repeatedSelector,
        ".empty-state",
        ".operations-empty"
    ].join(",");

    let intersectionObserver;
    let mutationObserver;
    let flushScheduled = false;

    function isReduced() {
        return motionQuery.matches;
    }

    function effectFor(element) {
        if (element.matches(".page-header")) {
            return {
                frames: [
                    { opacity: 0, transform: "translateY(-4px)" },
                    { opacity: 1, transform: "translateY(0)" }
                ],
                duration: 210
            };
        }

        if (element.matches(rowSelector)) {
            return {
                frames: [
                    { opacity: 0, transform: "translateX(5px)" },
                    { opacity: 1, transform: "translateX(0)" }
                ],
                duration: 160
            };
        }

        return {
            frames: [
                { opacity: 0, transform: "translateY(6px)" },
                { opacity: 1, transform: "translateY(0)" }
            ],
            duration: 190
        };
    }

    function play(element) {
        if (!(element instanceof HTMLElement) || animated.has(element)) return;
        animated.add(element);
        if (isReduced() || element.hidden) return;

        const effect = effectFor(element);
        const animation = element.animate(effect.frames, {
            duration: constrainedDevice ? Math.round(effect.duration * .8) : effect.duration,
            delay: delays.get(element) || 0,
            easing: "cubic-bezier(.2, .75, .25, 1)",
            fill: "backwards"
        });
        animation.finished
            .catch(() => { })
            .finally(() => animation.cancel());
    }

    function observeElement(element, delay) {
        if (animated.has(element)) return;
        delays.set(element, delay);
        if (intersectionObserver) intersectionObserver.observe(element);
        else play(element);
    }

    function candidatesFor(root) {
        const candidates = [];
        if (root instanceof Element && root.matches(selector)) candidates.push(root);
        root.querySelectorAll(selector).forEach(element => candidates.push(element));
        return candidates;
    }

    function registerRoots(roots) {
        const candidates = Array.from(new Set(
            roots
                .filter(root => root instanceof Element || root instanceof Document)
                .flatMap(candidatesFor)
        ));
        const candidateSet = new Set(candidates);
        const siblingIndexes = new Map();

        for (const element of candidates) {
            if (animated.has(element)) continue;

            const animatedAncestor = element.parentElement?.closest(selector);
            if (element.matches(rowSelector) && animatedAncestor && candidateSet.has(animatedAncestor)) {
                animated.add(element);
                continue;
            }

            const parent = element.parentElement;
            const index = siblingIndexes.get(parent) || 0;
            siblingIndexes.set(parent, index + 1);
            if (index >= maxAnimatedSiblings) {
                animated.add(element);
                continue;
            }
            observeElement(element, index * staggerStep);
        }
    }

    function compactPendingRoots() {
        const roots = Array.from(pendingRoots);
        const rootSet = new Set(roots);
        pendingRoots.clear();
        return roots.filter(root => {
            let parent = root.parentElement;
            while (parent) {
                if (rootSet.has(parent)) return false;
                parent = parent.parentElement;
            }
            return true;
        });
    }

    function flush() {
        flushScheduled = false;
        registerRoots(compactPendingRoots());
    }

    function scheduleRoot(root) {
        if (root instanceof Element || root instanceof Document) pendingRoots.add(root);
        if (flushScheduled) return;
        flushScheduled = true;
        window.requestAnimationFrame(flush);
    }

    function start() {
        document.documentElement.classList.add("motion-ready");
        intersectionObserver = new IntersectionObserver(entries => {
            entries.forEach(entry => {
                if (!entry.isIntersecting) return;
                intersectionObserver.unobserve(entry.target);
                play(entry.target);
            });
        }, { threshold: 0.04, rootMargin: "0px 0px 24px 0px" });

        mutationObserver = new MutationObserver(records => {
            for (const record of records) {
                for (const node of record.addedNodes) {
                    if (node instanceof Element) pendingRoots.add(node);
                }
            }
            if (pendingRoots.size > 0 && !flushScheduled) {
                flushScheduled = true;
                window.requestAnimationFrame(flush);
            }
        });
        mutationObserver.observe(document.body, { childList: true, subtree: true });
        scheduleRoot(document);
    }

    async function exitById(id) {
        const element = document.getElementById(id);
        if (!element || isReduced()) return;
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
            // Blazor may replace the element before the animation completes.
        }
    }

    window.portalMotion = {
        refresh: () => scheduleRoot(document),
        exitById
    };

    document.addEventListener("enhancedload", () => scheduleRoot(document));
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", start, { once: true });
    } else {
        start();
    }
})();
