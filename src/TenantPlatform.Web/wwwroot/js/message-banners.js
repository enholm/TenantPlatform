(() => {
    "use strict";

    const selector = [
        ".alert-success",
        ".alert-warning",
        ".alert-danger",
        '.alert-info[role="alert"]',
        '.alert-info[role="status"]'
    ].join(",");
    const defaultTimeoutMilliseconds = 10_000;
    const states = new WeakMap();

    function timeoutMilliseconds() {
        const configured = Number(document.body?.dataset.messageBannerTimeout);
        return Number.isFinite(configured) && configured >= 0
            ? configured
            : defaultTimeoutMilliseconds;
    }

    function closeLabel() {
        return document.body?.dataset.messageCloseLabel || "Close message";
    }

    function isCandidate(element) {
        return element instanceof HTMLElement && element.matches(selector) &&
            element.dataset.messageBanner !== "off";
    }

    function signature(element) {
        const copy = element.cloneNode(true);
        copy.querySelectorAll("[data-message-banner-close]").forEach(button => button.remove());
        const tone = ["alert-success", "alert-warning", "alert-danger", "alert-info"]
            .find(name => element.classList.contains(name)) || "";
        return `${tone}|${element.dataset.messageId || ""}|${copy.textContent?.trim() || ""}`;
    }

    function cancelTimer(state) {
        if (state.timer !== null) {
            window.clearTimeout(state.timer);
            state.timer = null;
        }
    }

    function hide(element, state) {
        cancelTimer(state);
        element.hidden = true;
    }

    function startTimer(element, state) {
        cancelTimer(state);
        state.timer = window.setTimeout(() => hide(element, state), timeoutMilliseconds());
    }

    function ensureButton(element, state) {
        if (state.button?.parentElement === element) {
            state.button.setAttribute("aria-label", closeLabel());
            return;
        }
        element.querySelectorAll("[data-message-banner-close]").forEach(button => button.remove());
        const button = document.createElement("button");
        button.type = "button";
        button.className = "btn-close tp-message-banner-close";
        button.dataset.messageBannerClose = "";
        button.setAttribute("aria-label", closeLabel());
        button.addEventListener("click", () => hide(element, state));
        state.button = button;
        element.append(button);
    }

    function enhance(element) {
        if (!isCandidate(element)) return;
        let state = states.get(element);
        if (!state) {
            state = { button: null, timer: null, signature: null };
            states.set(element, state);
        }
        if (!element.classList.contains("tp-message-banner"))
            element.classList.add("tp-message-banner");
        ensureButton(element, state);
        const nextSignature = signature(element);
        if (state.signature === nextSignature) return;
        state.signature = nextSignature;
        element.hidden = false;
        startTimer(element, state);
    }

    function visit(node) {
        if (!(node instanceof Element)) return;
        if (isCandidate(node)) enhance(node);
        node.querySelectorAll(selector).forEach(enhance);
    }

    function cleanup(node) {
        if (!(node instanceof Element)) return;
        const candidates = node.matches(selector)
            ? [node, ...node.querySelectorAll(selector)]
            : [...node.querySelectorAll(selector)];
        for (const element of candidates) {
            const state = states.get(element);
            if (!state) continue;
            cancelTimer(state);
            states.delete(element);
        }
    }

    const observer = new MutationObserver(mutations => {
        const affected = new Set();
        for (const mutation of mutations) {
            mutation.removedNodes.forEach(cleanup);
            mutation.addedNodes.forEach(visit);
            const element = mutation.target instanceof Element
                ? mutation.target
                : mutation.target.parentElement;
            const alert = element?.closest(selector);
            if (alert) affected.add(alert);
        }
        affected.forEach(enhance);
    });

    function initialize() {
        document.querySelectorAll(selector).forEach(enhance);
        observer.observe(document.body, {
            childList: true,
            subtree: true,
            characterData: true,
            attributes: true,
            attributeFilter: ["class", "data-message-id", "data-message-banner"]
        });
    }

    if (document.readyState === "loading")
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    else
        initialize();
})();
