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
