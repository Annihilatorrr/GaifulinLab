(() => {
    document.addEventListener("pointerdown", (event) => {
        document
            .querySelectorAll("details[data-dismiss-on-outside-click][open]")
            .forEach((popover) => {
                if (!popover.contains(event.target)) {
                    popover.open = false;
                }
            });
    });

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape") {
            return;
        }

        const popover = event.target.closest("details[data-dismiss-on-outside-click][open]");
        if (!popover) {
            return;
        }

        event.preventDefault();
        popover.open = false;
        popover.querySelector("summary")?.focus();
    });
})();
