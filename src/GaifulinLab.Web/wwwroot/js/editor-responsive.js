(() => {
    let editorReference;
    let compactLayoutQuery;

    const report = () => {
        if (!document.querySelector(".article-editor")) {
            return;
        }

        editorReference?.invokeMethodAsync("SetCompactLayoutAsync", compactLayoutQuery.matches).catch(() => {
            // The editor may have been disposed while a viewport change was handled.
        });
    };

    const attach = (reference) => {
        editorReference = reference;
        if (!compactLayoutQuery) {
            compactLayoutQuery = window.matchMedia("(max-width: 52rem)");
            compactLayoutQuery.addEventListener("change", report);
        }

        report();
    };

    window.gaifulinLabEditorResponsive = { attach };
})();
