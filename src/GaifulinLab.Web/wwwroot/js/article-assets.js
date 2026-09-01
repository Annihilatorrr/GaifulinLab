(() => {
    const mathJaxUrl = "https://cdn.jsdelivr.net/npm/mathjax@3.2.2/es5/tex-chtml.js";
    let mathJaxLoadPromise;

    const prepare = (root, apiBaseUrl) => {
        root.querySelectorAll('img[src^="/media/"]').forEach((image) => {
            image.src = new URL(image.getAttribute("src"), apiBaseUrl).toString();
        });
    };

    const download = (url) => window.location.assign(url);

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
            script.onload = () => {
                const mathJax = window.MathJax;
                if (!mathJax?.typesetPromise) {
                    reject(new Error("MathJax did not initialize."));
                    return;
                }

                Promise.resolve(mathJax.startup?.promise).then(() => resolve(mathJax), reject);
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
