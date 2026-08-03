window.condotifyCftv = (() => {
    const sessions = new Map();
    const requestTimeoutMs = 12000;
    const connectionTimeoutMs = 15000;

    function waitForIceGathering(peer) {
        if (peer.iceGatheringState === "complete") return Promise.resolve();
        return new Promise(resolve => {
            const timeout = setTimeout(done, 2500);
            function done() {
                clearTimeout(timeout);
                peer.removeEventListener("icegatheringstatechange", changed);
                resolve();
            }
            function changed() {
                if (peer.iceGatheringState === "complete") done();
            }
            peer.addEventListener("icegatheringstatechange", changed);
        });
    }

    function waitForConnection(peer) {
        if (peer.connectionState === "connected") return Promise.resolve();
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => finish(new Error("Tempo limite ao conectar o video.")), connectionTimeoutMs);
            function finish(error) {
                clearTimeout(timeout);
                peer.removeEventListener("connectionstatechange", changed);
                error ? reject(error) : resolve();
            }
            function changed() {
                if (peer.connectionState === "connected") finish();
                else if (peer.connectionState === "failed" || peer.connectionState === "closed")
                    finish(new Error("A conexao de video foi recusada."));
            }
            peer.addEventListener("connectionstatechange", changed);
        });
    }

    async function start(elementId, url) {
        await stop(elementId);
        const video = document.getElementById(elementId);
        if (!video) throw new Error("O elemento de video nao foi encontrado.");

        const peer = new RTCPeerConnection();
        const controller = new AbortController();
        sessions.set(elementId, { peer, video, controller });
        peer.addTransceiver("video", { direction: "recvonly" });
        peer.ontrack = event => {
            video.srcObject = event.streams[0] ?? new MediaStream([event.track]);
        };

        try {
            const offer = await peer.createOffer();
            await peer.setLocalDescription(offer);
            await waitForIceGathering(peer);

            const requestTimeout = setTimeout(() => controller.abort(), requestTimeoutMs);
            let response;
            try {
                response = await fetch(url, {
                    method: "POST",
                    headers: { "Content-Type": "application/sdp" },
                    body: peer.localDescription.sdp,
                    signal: controller.signal
                });
            } finally {
                clearTimeout(requestTimeout);
            }

            const answer = await response.text();
            if (!response.ok) throw new Error(`O gateway de video respondeu HTTP ${response.status}.`);
            if (!answer.trim()) throw new Error("O gateway retornou uma negociacao vazia.");

            await peer.setRemoteDescription({ type: "answer", sdp: answer });
            await waitForConnection(peer);
            video.muted = true;
            await video.play();
        } catch (error) {
            const current = sessions.get(elementId);
            if (current?.peer === peer) sessions.delete(elementId);
            controller.abort();
            peer.close();
            video.srcObject = null;
            if (error?.name === "AbortError") throw new Error("O gateway de video demorou para responder.");
            throw error;
        }
    }

    async function stop(elementId) {
        const session = sessions.get(elementId);
        if (!session) return;
        session.controller.abort();
        session.peer.close();
        session.video.pause();
        session.video.srcObject = null;
        sessions.delete(elementId);
    }

    return { start, stop };
})();
