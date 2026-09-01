(() => {
    const prepare = (root, apiBaseUrl) => {
        root.querySelectorAll('img[src^="/media/"]').forEach((image) => {
            image.src = new URL(image.getAttribute("src"), apiBaseUrl).toString();
        });
    };

    window.articleAssets = { prepare };
})();
