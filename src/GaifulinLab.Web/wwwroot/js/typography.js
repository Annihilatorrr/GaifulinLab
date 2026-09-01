(() => {
    const storageKey = "gaifulinlab-article-typography";
    const defaults = { lineHeight: 1.75, blockSpacing: 0.75 };
    const clamp = (value, min, max, fallback) => {
        const number = Number(value);
        return Number.isFinite(number) ? Math.min(max, Math.max(min, number)) : fallback;
    };
    const normalize = (value) => ({
        lineHeight: clamp(value?.lineHeight, 1, 2.2, defaults.lineHeight),
        blockSpacing: clamp(value?.blockSpacing, 0.1, 1.6, defaults.blockSpacing)
    });

    const get = () => {
        try {
            const raw = localStorage.getItem(storageKey);
            return normalize(raw ? JSON.parse(raw) : defaults);
        } catch {
            return { ...defaults };
        }
    };

    const set = (value) => {
        const normalized = normalize(value);
        try {
            localStorage.setItem(storageKey, JSON.stringify(normalized));
        } catch {
            // A blocked storage API must not prevent adjusting the current page.
        }
        return normalized;
    };

    window.gaifulinLabTypography = { get, set };
})();
