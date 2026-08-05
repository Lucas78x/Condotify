window.portalInterop = {
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
    },
    scrollHorizontal: function (element, direction) {
        if (!element) return;
        const distance = Math.max(260, element.clientWidth * 0.72);
        element.scrollBy({ left: distance * Math.sign(direction || 1), behavior: "smooth" });
    },
    centerActiveNavItem: function (element) {
        if (!element) return;
        const activeItem = element.querySelector(".mud-button-filled");
        if (!activeItem) return;
        activeItem.scrollIntoView({ behavior: "smooth", block: "nearest", inline: "center" });
    }
};
