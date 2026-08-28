window.portalInterop = {
    shellKey: "ff-access.sidebar.compact",
    shellHandler: null,
    shellScrollHandler: null,
    shellScrollQueued: false,
    initializeShell: function (dotNetReference) {
        this.disposeShell();
        this.shellHandler = function (event) {
            if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
                event.preventDefault();
                dotNetReference.invokeMethodAsync("ToggleCommandPaletteFromKeyboardAsync");
            }
        };
        document.addEventListener("keydown", this.shellHandler);
        this.shellScrollHandler = () => {
            if (this.shellScrollQueued) return;
            this.shellScrollQueued = true;
            window.requestAnimationFrame(() => {
                document.body.classList.toggle("portal-scrolled", window.scrollY > 112);
                this.shellScrollQueued = false;
            });
        };
        window.addEventListener("scroll", this.shellScrollHandler, { passive: true });
        this.shellScrollHandler();
        return localStorage.getItem(this.shellKey) === "true";
    },
    disposeShell: function () {
        if (this.shellHandler) document.removeEventListener("keydown", this.shellHandler);
        if (this.shellScrollHandler) window.removeEventListener("scroll", this.shellScrollHandler);
        this.shellHandler = null;
        this.shellScrollHandler = null;
        document.body.classList.remove("portal-scrolled");
    },
    setSidebarCompact: function (compact) {
        localStorage.setItem(this.shellKey, compact ? "true" : "false");
    },
    focusElement: function (element) {
        if (element instanceof HTMLElement) element.focus({ preventScroll: true });
    },
    scrollToBottom: function (element) {
        if (element instanceof HTMLElement) element.scrollTop = element.scrollHeight;
    },
    submitLogout: function () {
        const form = document.getElementById("portal-logout-form");
        if (form instanceof HTMLFormElement) form.requestSubmit();
    },
    downloadText: function (fileName, content, contentType) {
        const blob = new Blob([content], { type: contentType || "text/plain" });
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = fileName;
        anchor.click();
        URL.revokeObjectURL(url);
    },
    downloadBase64: function (fileName, contentBase64, contentType) {
        const binary = atob(contentBase64);
        const bytes = new Uint8Array(binary.length);
        for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
        const blob = new Blob([bytes], { type: contentType || "application/octet-stream" });
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = fileName;
        anchor.click();
        URL.revokeObjectURL(url);
    },
    analyzeFacePhoto: async function (dataUrl) {
        const image = new Image();
        image.src = dataUrl;
        await image.decode();
        const result = { supported: "FaceDetector" in window, faceCount: -1, width: image.naturalWidth, height: image.naturalHeight };
        if (!result.supported) return result;
        try {
            const detector = new FaceDetector({ fastMode: true, maxDetectedFaces: 2 });
            const faces = await detector.detect(image);
            result.faceCount = faces.length;
        } catch {
            result.supported = false;
        }
        return result;
    }
};
