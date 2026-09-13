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
        editor.dataset.htmlSelectionStart = String(editor.selectionStart ?? 0);
        editor.dataset.htmlSelectionEnd = String(editor.selectionEnd ?? 0);
    };

    const attach = (editor) => {
        if (!editor || editor.dataset.htmlEnhancementsAttached) {
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
        editor.dataset.htmlEnhancementsAttached = "true";
        rememberSelection(editor);
    };

    const insertAtSelection = (editor, html) => {
        if (!editor) {
            return html;
        }

        const start = Number(editor.dataset.htmlSelectionStart ?? editor.selectionStart ?? editor.value.length);
        const end = Number(editor.dataset.htmlSelectionEnd ?? editor.selectionEnd ?? start);
        const before = editor.value.slice(0, start);
        const after = editor.value.slice(end);
        const prefix = before.length && !before.endsWith("\n") ? "\n\n" : "";
        const suffix = after.length && !after.startsWith("\n") ? "\n\n" : "";
        const insertion = `${prefix}${html}${suffix}`;

        editor.setRangeText(insertion, start, end, "end");
        rememberSelection(editor);
        editor.focus();
        return editor.value;
    };

    const escapeHtml = (value) => value
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");

    const insertCodeBlock = (editor, language) => {
        if (!editor) {
            return "<pre><code class=\"language-plaintext\">code</code></pre>";
        }

        const start = Number(editor.dataset.htmlSelectionStart ?? editor.selectionStart ?? editor.value.length);
        const end = Number(editor.dataset.htmlSelectionEnd ?? editor.selectionEnd ?? start);
        const selection = editor.value.slice(start, end) || "code";
        const normalizedLanguage = String(language ?? "plaintext").replace(/[^a-z0-9-]/gi, "") || "plaintext";
        return insertAtSelection(editor, `<pre><code class="language-${normalizedLanguage}">${escapeHtml(selection)}</code></pre>`);
    };

    window.gaifulinLabHtmlEditor = { attach, insertAtSelection, insertCodeBlock };
})();
