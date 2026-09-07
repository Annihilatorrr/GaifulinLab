(() => {
    const mathJaxUrl = "https://cdn.jsdelivr.net/npm/mathjax@4.1.3/tex-chtml.js";
    let mathJaxLoadPromise;

    const prepare = (root, apiBaseUrl) => {
        root.querySelectorAll('img[src^="/media/"]').forEach((image) => {
            image.src = new URL(image.getAttribute("src"), apiBaseUrl).toString();
        });
    };

    const download = (content, fileName) => {
        const url = URL.createObjectURL(new Blob([content], { type: "application/pdf" }));
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
    };

    const loadMathJax = () => {
        if (window.MathJax?.typesetPromise) {
            return Promise.resolve(window.MathJax);
        }

        if (mathJaxLoadPromise) {
            return mathJaxLoadPromise;
        }

        window.MathJax = {
            startup: {
                typeset: false
            }
        };

        mathJaxLoadPromise = new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = mathJaxUrl;
            script.async = true;
            script.onload = async () => {
                const mathJax = window.MathJax;
                if (!mathJax?.startup?.promise) {
                    reject(new Error("MathJax did not initialize."));
                    return;
                }

                try {
                    if (typeof mathJax.typesetPromise !== "function") {
                        mathJax.startup.defaultReady();
                    }

                    await mathJax.startup.promise;
                    if (typeof mathJax.typesetPromise !== "function") {
                        throw new Error("MathJax typesetting API did not initialize.");
                    }

                    resolve(mathJax);
                } catch (error) {
                    reject(error);
                }
            };
            script.onerror = () => reject(new Error("MathJax could not be loaded."));
            document.head.appendChild(script);
        }).catch((error) => {
            mathJaxLoadPromise = undefined;
            throw error;
        });

        return mathJaxLoadPromise;
    };

    const typesetMath = async (root) => {
        const mathJax = await loadMathJax();
        mathJax.typesetClear([root]);
        mathJax.texReset();
        await mathJax.typesetPromise([root]);
    };

    const clearMath = (root) => {
        const mathJax = window.MathJax;
        if (!mathJax?.typesetPromise) {
            return;
        }

        mathJax.typesetClear([root]);
        mathJax.texReset();
    };

    window.articleAssets = { prepare, download, typesetMath, clearMath };
})();
