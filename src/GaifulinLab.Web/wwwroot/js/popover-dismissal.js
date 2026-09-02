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
})();
