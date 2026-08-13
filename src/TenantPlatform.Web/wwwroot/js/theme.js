window.tenantPlatformTheme = {
    storageKey: "tenantplatform-theme",

    set: function (theme) {
        localStorage.setItem(this.storageKey, theme);
        this.apply(theme);
    },

    get: function () {
        return localStorage.getItem(this.storageKey) || "system";
    },

    apply: function (theme) {
        let effectiveTheme = theme;

        if (theme === "system") {
            effectiveTheme =
                window.matchMedia("(prefers-color-scheme: dark)").matches
                    ? "dark"
                    : "light";
        }

        document.documentElement.setAttribute(
            "data-bs-theme",
            effectiveTheme);
    },

    applyCurrent: function () {
        this.apply(this.get());
    },

    initialize: function () {
        this.applyCurrent();

        window.matchMedia("(prefers-color-scheme: dark)")
            .addEventListener("change", () => {
                if (this.get() === "system") {
                    this.applyCurrent();
                }
            });
    }
};

window.tenantPlatformTheme.initialize();

Blazor.addEventListener("enhancedload", () => {
    window.tenantPlatformTheme.applyCurrent();
});
