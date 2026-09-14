(() => {
    const storageKey = "GaifulinLab.Web.UiCulture";

    function preferredLocale() {
        const candidates = navigator.languages?.length ? navigator.languages : [navigator.language || "ru-RU"];
        return candidates.find(value => tryNormalize(value) !== null) || "ru-RU";
    }

    function tryNormalize(value) {
        if (!/^(ru|en)(?:-[a-z0-9]{2,8})*$/i.test(value || "")) return null;
        try {
            const language = Intl.getCanonicalLocales(value)[0]?.split("-")[0]?.toLowerCase();
            return language === "en" || language === "ru" ? language : null;
        } catch {
            return null;
        }
    }

    function normalize(value) {
        return tryNormalize(value) || "ru";
    }

    function readStorage(key) {
        try {
            return localStorage.getItem(key);
        } catch {
            return null;
        }
    }

    function writeStorage(key, value) {
        try {
            localStorage.setItem(key, value);
        } catch {
            // The selected language still applies to the current document.
        }
    }

    const initialCode = tryNormalize(readStorage(storageKey)) || normalize(preferredLocale());
    document.documentElement.lang = initialCode;
    fetch(`i18n/${initialCode}.json`)
        .then(response => response.ok ? response.json() : {})
        .then(strings => {
            document.querySelectorAll("[data-i18n]").forEach(element => {
                const value = strings[element.dataset.i18n];
                if (value) element.textContent = value;
            });
            document.querySelectorAll("[data-i18n-aria-label]").forEach(element => {
                const value = strings[element.dataset.i18nAriaLabel];
                if (value) element.setAttribute("aria-label", value);
            });
        })
        .catch(() => {});

    window.GaifulinLab = window.GaifulinLab || {};
    window.GaifulinLab.uiCulture = {
        get: readStorage,
        getPreferred: () => normalize(preferredLocale()),
        getPreferredLocale: preferredLocale,
        apply: value => { document.documentElement.lang = normalize(value); },
        set: (key, value) => {
            writeStorage(key, normalize(value));
            document.documentElement.lang = normalize(value);
        },
        navigateForCulture: value => {
            const code = normalize(value);
            const path = window.location.pathname;
            if (/^\/(ru|en)\/articles\//i.test(path)) {
                const alternate = document.querySelector(`[data-article-localization-language="${code}"]`);
                window.location.assign(alternate?.href || "/articles");
                return;
            }
            if (/^\/(ru|en)\/series\//i.test(path)) {
                window.location.assign("/series");
                return;
            }
            if (/^\/search\/?$/i.test(path)) {
                const url = new URL(window.location.href);
                ["languageCode", "page", "topic", "tag"].forEach(name => url.searchParams.delete(name));
                window.location.assign(url.pathname + url.search);
                return;
            }
            if (/^\/articles\/?$/i.test(path)) {
                const url = new URL(window.location.href);
                ["page", "topic", "series", "tag"].forEach(name => url.searchParams.delete(name));
                window.location.assign(url.pathname + url.search);
                return;
            }
            window.location.reload();
        }
    };
})();
