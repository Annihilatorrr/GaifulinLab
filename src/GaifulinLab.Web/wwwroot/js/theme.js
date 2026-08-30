(() => {
    const storageKey = "gaifulinlab-theme";
    const root = document.documentElement;
    const media = window.matchMedia("(prefers-color-scheme: dark)");

    const readStoredTheme = () => {
        try {
            const theme = localStorage.getItem(storageKey);
            return theme === "light" || theme === "dark" ? theme : null;
        } catch {
            return null;
        }
    };

    const saveTheme = (theme) => {
        try {
            localStorage.setItem(storageKey, theme);
        } catch {
            // A blocked storage API must not prevent switching the current page.
        }
    };

    const storedTheme = readStoredTheme();
    if (storedTheme) {
        root.dataset.theme = storedTheme;
    }

    const currentTheme = () => root.dataset.theme ?? (media.matches ? "dark" : "light");

    window.gaifulinLabTheme = {
        getCurrent: currentTheme,
        toggle: () => {
            const nextTheme = currentTheme() === "dark" ? "light" : "dark";
            root.dataset.theme = nextTheme;
            saveTheme(nextTheme);
            return nextTheme;
        }
    };
})();
