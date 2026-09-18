(() => {
    const mathJaxUrl = "https://cdn.jsdelivr.net/npm/mathjax@4.1.3/tex-chtml.js";
    let mathJaxLoadPromise;

    const prepare = (root, apiBaseUrl, copyLabels) => {
        root.querySelectorAll('img[src^="/media/"]').forEach((image) => {
            image.src = new URL(image.getAttribute("src"), apiBaseUrl).toString();
        });

        root.querySelectorAll("pre code[class*='language-']").forEach((code) => {
            try {
                window.hljs?.highlightElement(code);
            } catch {
                // A typo in a language class must leave the original code readable.
            }
        });

        if (copyLabels) {
            prepareCodeCopy(root, copyLabels);
        }

        prepareTableOfContents(root);
    };

    const prepareCodeCopy = (root, labels) => {
        root.querySelectorAll("pre > code").forEach((code) => {
            const pre = code.parentElement;
            if (pre.dataset.articleCodeCopy === "true") {
                return;
            }

            const wrapper = document.createElement("div");
            wrapper.className = "article-code-block";
            pre.before(wrapper);
            wrapper.append(pre);

            const button = document.createElement("button");
            button.type = "button";
            button.className = "article-code-copy";
            button.setAttribute("aria-label", labels.copyCode);
            button.title = labels.copyCode;
            button.append(createCopyIcon("copy"));

            const status = document.createElement("span");
            status.className = "visually-hidden";
            status.setAttribute("role", "status");
            status.setAttribute("aria-live", "polite");

            let feedbackTimer;
            const showFeedback = (state, message) => {
                window.clearTimeout(feedbackTimer);
                button.dataset.copyState = state;
                button.dataset.copyFeedback = message;
                button.setAttribute("aria-label", message);
                button.title = message;
                button.replaceChildren(createCopyIcon(state));
                status.textContent = message;
                feedbackTimer = window.setTimeout(() => {
                    delete button.dataset.copyState;
                    delete button.dataset.copyFeedback;
                    button.setAttribute("aria-label", labels.copyCode);
                    button.title = labels.copyCode;
                    button.replaceChildren(createCopyIcon("copy"));
                    status.textContent = "";
                }, 2000);
            };

            button.addEventListener("click", async () => {
                try {
                    if (!navigator.clipboard?.writeText) {
                        throw new Error("Clipboard API is unavailable.");
                    }

                    await navigator.clipboard.writeText(code.textContent ?? "");
                    showFeedback("success", labels.codeCopied);
                } catch {
                    showFeedback("error", labels.copyCodeFailed);
                }
            });

            wrapper.append(button, status);
            pre.dataset.articleCodeCopy = "true";
        });
    };

    const createCopyIcon = (state) => {
        const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
        svg.setAttribute("viewBox", "0 0 24 24");
        svg.setAttribute("aria-hidden", "true");
        svg.setAttribute("focusable", "false");

        const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
        path.setAttribute("fill", "none");
        path.setAttribute("stroke", "currentColor");
        path.setAttribute("stroke-linecap", "round");
        path.setAttribute("stroke-linejoin", "round");
        path.setAttribute("stroke-width", "2");
        path.setAttribute("d", state === "success"
            ? "m5 12 4 4L19 6"
            : state === "error"
                ? "m6 6 12 12M18 6 6 18"
                : "M9 8h10v12H9zM5 4h10v4M5 4v12h4");
        svg.append(path);
        return svg;
    };

    const prepareTableOfContents = (root) => {
        const links = root.querySelectorAll('.article-toc > ol > li > a[href^="#"]');
        if (!links.length) {
            return;
        }

        const pathWithSearch = `${window.location.pathname}${window.location.search}`;
        links.forEach((link) => {
            const rawFragment = link.getAttribute("href");
            if (!rawFragment || rawFragment.length === 1) {
                return;
            }

            const targetId = rawFragment.slice(1);
            const fragment = `#${encodeURIComponent(targetId)}`;
            link.setAttribute("href", `${pathWithSearch}${fragment}`);
            link.addEventListener("click", (event) => {
                if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
                    return;
                }

                const target = root.querySelector(`#${CSS.escape(targetId)}`);
                if (!target) {
                    return;
                }

                event.preventDefault();
                window.history.pushState(null, "", `${pathWithSearch}${fragment}`);
                target.scrollIntoView({ block: "start" });
            });
        });

        const rawInitialTargetId = window.location.hash.slice(1);
        if (!rawInitialTargetId) {
            return;
        }

        let initialTargetId;
        try {
            initialTargetId = decodeURIComponent(rawInitialTargetId);
        } catch {
            return;
        }

        const initialTarget = root.querySelector(`#${CSS.escape(initialTargetId)}`);
        if (initialTarget) {
            requestAnimationFrame(() => initialTarget.scrollIntoView({ block: "start" }));
        }
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
