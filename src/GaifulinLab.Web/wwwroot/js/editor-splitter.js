(() => {
    const storageKey = "gaifulinlab-editor-split";
    const defaultSplit = 50;
    const minSplit = 30;
    const maxSplit = 70;
    let activeCleanup;

    const clamp = (value) => {
        const number = Number(value);
        return Number.isFinite(number) ? Math.min(maxSplit, Math.max(minSplit, number)) : defaultSplit;
    };

    const apply = (workspace, value) => {
        const split = clamp(value);
        workspace.style.setProperty("--editor-split", `${split}%`);
        workspace.style.setProperty("--editor-preview-split", `${100 - split}%`);
        return split;
    };

    const get = () => {
        try {
            return clamp(localStorage.getItem(storageKey));
        } catch {
            return defaultSplit;
        }
    };

    const set = (value) => {
        const split = clamp(value);
        try {
            localStorage.setItem(storageKey, String(split));
        } catch {
            // A blocked storage API must not prevent changing the current layout.
        }
        return split;
    };

    const start = (workspace, initialClientX, dotNetReference) => {
        if (!workspace || !dotNetReference) {
            return;
        }

        activeCleanup?.();
        const bounds = workspace.getBoundingClientRect();
        const update = (clientX) => apply(workspace, ((clientX - bounds.left) / bounds.width) * 100);
        let split = update(initialClientX);
        const onMove = (event) => {
            split = update(event.clientX);
        };
        const finish = () => {
            activeCleanup?.();
            dotNetReference.invokeMethodAsync("SetEditorSplitAsync", split).catch(() => {
                // The component may have been disposed while a drag was in progress.
            });
        };

        activeCleanup = () => {
            window.removeEventListener("pointermove", onMove);
            window.removeEventListener("pointerup", finish);
            window.removeEventListener("pointercancel", finish);
            activeCleanup = undefined;
        };

        window.addEventListener("pointermove", onMove);
        window.addEventListener("pointerup", finish, { once: true });
        window.addEventListener("pointercancel", finish, { once: true });
    };

    window.gaifulinLabEditorSplitter = { get, set, start };
})();
