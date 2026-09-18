window.Blazor.start({
    loadBootResource: function (type, name, defaultUri, integrity) {
        return `${defaultUri}${defaultUri.includes("?") ? "&" : "?"}v=__BUILD_VERSION__`;
    }
});
