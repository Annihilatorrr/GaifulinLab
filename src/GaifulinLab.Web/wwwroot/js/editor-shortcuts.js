(() => {
    let editorReference;
    let listening = false;

    const onKeyDown = (event) => {
        if (!document.querySelector(".article-editor")
            || !(event.ctrlKey || event.metaKey)
            || event.key.toLowerCase() !== "s") {
            return;
        }

        event.preventDefault();
        editorReference?.invokeMethodAsync("SaveArticleAsync").catch(() => {
            // The current editor may have been disposed while the shortcut was pressed.
        });
    };

    const attach = (reference) => {
        editorReference = reference;
        if (!listening) {
            document.addEventListener("keydown", onKeyDown);
            listening = true;
        }
    };

    window.gaifulinLabEditorShortcuts = { attach };
})();
