(() => {
    const storageKey = "gaifulinlab-theme";
    const root = document.documentElement;
    const media = window.matchMedia("(prefers-color-scheme: dark)");

    const storedTheme = localStorage.getItem(storageKey);
    if (storedTheme === "light" || storedTheme === "dark") {
        root.dataset.theme = storedTheme;
    }

    const currentTheme = () => root.dataset.theme ?? (media.matches ? "dark" : "light");

    const updateButton = (button) => {
        const isDark = currentTheme() === "dark";
        button.setAttribute("aria-pressed", String(isDark));
        button.setAttribute("aria-label", isDark ? "Switch to light theme" : "Switch to dark theme");
    };

    const bindThemeToggle = () => {
        const button = document.querySelector("[data-theme-toggle]");
        if (!button || button.dataset.themeBound === "true") {
            return;
        }

        button.dataset.themeBound = "true";
        updateButton(button);
        button.addEventListener("click", () => {
            const nextTheme = currentTheme() === "dark" ? "light" : "dark";
            root.dataset.theme = nextTheme;
            localStorage.setItem(storageKey, nextTheme);
            updateButton(button);
        });
    };

    document.addEventListener("DOMContentLoaded", bindThemeToggle);
    document.addEventListener("enhancedload", bindThemeToggle);
})();
