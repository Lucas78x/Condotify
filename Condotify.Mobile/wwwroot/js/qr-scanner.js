window.condotifyQrScanner = (() => {
    let stream;
    let frameHandle;
    let detector;
    let stopping = false;

    async function stop(videoId) {
        stopping = true;
        if (frameHandle) cancelAnimationFrame(frameHandle);
        frameHandle = undefined;
        if (stream) stream.getTracks().forEach(track => track.stop());
        stream = undefined;
        const video = document.getElementById(videoId);
        if (video) video.srcObject = null;
    }

    async function start(videoId, callback) {
        await stop(videoId);
        stopping = false;
        const video = document.getElementById(videoId);
        if (!video) throw new Error("Visualizador da câmera não encontrado.");
        if (!navigator.mediaDevices?.getUserMedia)
            throw new Error("A câmera ao vivo não está disponível neste aparelho. Digite o código abaixo.");
        if (!("BarcodeDetector" in window))
            throw new Error("O leitor automático não está disponível neste Android. Digite o código abaixo.");

        detector = new BarcodeDetector({ formats: ["qr_code"] });
        stream = await navigator.mediaDevices.getUserMedia({
            audio: false,
            video: { facingMode: { ideal: "environment" }, width: { ideal: 1280 }, height: { ideal: 720 } }
        });
        video.srcObject = stream;
        await video.play();

        const scanFrame = async () => {
            if (stopping) return;
            try {
                const codes = await detector.detect(video);
                if (codes.length > 0 && codes[0].rawValue) {
                    stopping = true;
                    await callback.invokeMethodAsync("OnCodeDetected", codes[0].rawValue);
                    return;
                }
            } catch { }
            frameHandle = requestAnimationFrame(scanFrame);
        };
        frameHandle = requestAnimationFrame(scanFrame);
    }

    return { start, stop };
})();
