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

(() => {
    const rememberSelection = (editor) => {
        editor.dataset.markdownSelectionStart = String(editor.selectionStart ?? 0);
        editor.dataset.markdownSelectionEnd = String(editor.selectionEnd ?? 0);
    };

    const attach = (editor) => {
        if (!editor || editor.dataset.markdownEnhancementsAttached) {
            return;
        }

        const onKeyDown = (event) => {
            if (event.key !== "Tab") {
                return;
            }

            event.preventDefault();
            editor.setRangeText("    ", editor.selectionStart, editor.selectionEnd, "end");
            rememberSelection(editor);
            editor.dispatchEvent(new Event("input", { bubbles: true }));
        };

        editor.addEventListener("click", () => rememberSelection(editor));
        editor.addEventListener("focus", () => rememberSelection(editor));
        editor.addEventListener("input", () => rememberSelection(editor));
        editor.addEventListener("keyup", () => rememberSelection(editor));
        editor.addEventListener("select", () => rememberSelection(editor));
        editor.addEventListener("keydown", onKeyDown);
        editor.dataset.markdownEnhancementsAttached = "true";
        rememberSelection(editor);
    };

    const insertAtSelection = (editor, markdown) => {
        if (!editor) {
            return markdown;
        }

        const start = Number(editor.dataset.markdownSelectionStart ?? editor.selectionStart ?? editor.value.length);
        const end = Number(editor.dataset.markdownSelectionEnd ?? editor.selectionEnd ?? start);
        const before = editor.value.slice(0, start);
        const after = editor.value.slice(end);
        const prefix = before.length && !before.endsWith("\n") ? "\n\n" : "";
        const suffix = after.length && !after.startsWith("\n") ? "\n\n" : "";
        const insertion = `${prefix}${markdown}${suffix}`;

        editor.setRangeText(insertion, start, end, "end");
        rememberSelection(editor);
        editor.focus();
        return editor.value;
    };

    window.gaifulinLabMarkdownEditor = { attach, insertAtSelection };
})();
