window.condotifyCftv = (() => {
    const sessions = new Map();
    const requestTimeoutMs = 12000;
    const connectionTimeoutMs = 15000;

    function waitForIceGathering(peer) {
        if (peer.iceGatheringState === "complete") return Promise.resolve();
        return new Promise(resolve => {
            const timeout = setTimeout(done, 2500);
            function done() { clearTimeout(timeout); peer.removeEventListener("icegatheringstatechange", changed); resolve(); }
            function changed() { if (peer.iceGatheringState === "complete") done(); }
            peer.addEventListener("icegatheringstatechange", changed);
        });
    }

    function waitForConnection(peer) {
        if (peer.connectionState === "connected") return Promise.resolve();
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => finish(new Error("Tempo limite ao conectar o vídeo.")), connectionTimeoutMs);
            function finish(error) { clearTimeout(timeout); peer.removeEventListener("connectionstatechange", changed); error ? reject(error) : resolve(); }
            function changed() {
                if (peer.connectionState === "connected") finish();
                else if (peer.connectionState === "failed" || peer.connectionState === "closed") finish(new Error("A conexão de vídeo foi recusada."));
            }
            peer.addEventListener("connectionstatechange", changed);
        });
    }

    async function hasInboundAudio(peer, timeoutMs = 2200) {
        const deadline = Date.now() + timeoutMs;
        do {
            const stats = await peer.getStats();
            let receivedAudio = false;
            stats.forEach(report => {
                if (report.type === "inbound-rtp"
                    && (report.kind === "audio" || report.mediaType === "audio")
                    && ((report.packetsReceived ?? 0) > 0 || (report.bytesReceived ?? 0) > 0)) {
                    receivedAudio = true;
                }
            });
            if (receivedAudio) return true;
            if (Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 180));
        } while (Date.now() < deadline);
        return false;
    }

    async function start(elementId, url) {
        await stop(elementId);
        const video = document.getElementById(elementId);
        if (!video) throw new Error("O elemento de vídeo não foi encontrado.");
        const peer = new RTCPeerConnection();
        const controller = new AbortController();
        const mediaStream = new MediaStream();
        const session = { peer, video, controller, mediaStream, hasAudio: false, mode: "webrtc" };
        sessions.set(elementId, session);
        peer.addTransceiver("video", { direction: "recvonly" });
        peer.addTransceiver("audio", { direction: "recvonly" });
        peer.ontrack = event => {
            if (!mediaStream.getTracks().some(track => track.id === event.track.id)) mediaStream.addTrack(event.track);
            video.srcObject = mediaStream;
        };
        try {
            const offer = await peer.createOffer(); await peer.setLocalDescription(offer); await waitForIceGathering(peer);
            const requestTimeout = setTimeout(() => controller.abort(), requestTimeoutMs);
            let response;
            try { response = await fetch(url, { method: "POST", headers: { "Content-Type": "application/sdp" }, body: peer.localDescription.sdp, signal: controller.signal }); }
            finally { clearTimeout(requestTimeout); }
            const answer = await response.text();
            if (!response.ok) {
                const detail = answer.toLowerCase();
                if (detail.includes("codec") || detail.includes("h265") || detail.includes("h.265"))
                    throw new Error("O stream usa codec H.265 ou outro formato incompatível com o navegador.");
                if (response.status === 404)
                    throw new Error("O caminho de vídeo ainda não está disponível no gateway.");
                if (response.status === 401)
                    throw new Error("A autorização do vídeo expirou.");
                throw new Error("O gateway não conseguiu negociar a transmissão ao vivo.");
            }
            if (!answer.trim()) throw new Error("O gateway retornou uma negociação vazia.");
            await peer.setRemoteDescription({ type: "answer", sdp: answer });
            await waitForConnection(peer);
            video.muted = true;
            video.volume = 1;
            await video.play();
            session.hasAudio = await hasInboundAudio(peer);
            return {
                hasAudio: session.hasAudio,
                audioDeclared: mediaStream.getAudioTracks().some(track => track.readyState === "live")
            };
        } catch (error) {
            const current = sessions.get(elementId); if (current?.peer === peer) sessions.delete(elementId);
            controller.abort(); peer.close(); video.srcObject = null;
            if (error?.name === "AbortError") throw new Error("O gateway de vídeo demorou para responder.");
            throw error;
        }
    }

    async function startHls(elementId, url) {
        await stop(elementId);
        const video = document.getElementById(elementId);
        if (!video) throw new Error("O elemento de vídeo não foi encontrado.");

        video.muted = true;
        video.volume = 1;

        if (video.canPlayType("application/vnd.apple.mpegurl")) {
            const session = { video, hasAudio: true, mode: "hls-native" };
            sessions.set(elementId, session);
            video.src = url;
            await waitForMedia(video);
            await video.play();
            return { hasAudio: true };
        }

        if (!window.Hls || !window.Hls.isSupported())
            throw new Error("Este navegador não oferece reprodução HLS compatível.");

        const inheritedToken = new URL(url, document.baseURI).searchParams.get("token");
        const hls = new window.Hls({
            lowLatencyMode: true,
            liveSyncDurationCount: 2,
            liveMaxLatencyDurationCount: 5,
            maxLiveSyncPlaybackRate: 1.25,
            maxBufferLength: 8,
            backBufferLength: 0,
            xhrSetup: (xhr, requestUrl) => {
                // Playlists e fragmentos HLS usam URLs relativas. Reaplique o token
                // efêmero da sessão em cada requisição sem expô-lo no markup.
                const authorizedUrl = new URL(requestUrl, url);
                if (inheritedToken && !authorizedUrl.searchParams.has("token"))
                    authorizedUrl.searchParams.set("token", inheritedToken);
                xhr.open("GET", authorizedUrl.toString(), true);
            }
        });
        const session = { video, hls, hasAudio: false, mode: "hls" };
        sessions.set(elementId, session);
        hls.on(window.Hls.Events.BUFFER_CODECS, (_, data) => {
            const tracks = data?.tracks ?? data;
            if (tracks?.audio || tracks?.audiovideo) session.hasAudio = true;
        });

        try {
            const manifest = await new Promise((resolve, reject) => {
                const timeout = setTimeout(() => reject(new Error("Tempo limite ao preparar o vídeo com áudio.")), 15000);
                const finish = (callback, value) => { clearTimeout(timeout); callback(value); };
                hls.once(window.Hls.Events.MEDIA_ATTACHED, () => hls.loadSource(url));
                hls.once(window.Hls.Events.MANIFEST_PARSED, (_, data) => finish(resolve, data));
                hls.on(window.Hls.Events.ERROR, (_, data) => {
                    if (data.fatal) {
                        console.warn("[CFTV HLS] Falha fatal", data.type, data.details, data.response?.code ?? "");
                        finish(reject, new Error("O fluxo HLS da câmera não pôde ser reproduzido."));
                    }
                });
                hls.attachMedia(video);
            });
            const playPromise = video.play();
            session.hasAudio = await waitForHlsAudio(hls, manifest, session, video);
            await playPromise;
            // Este modo só é solicitado quando o WebRTC declarou uma faixa de
            // áudio sem receber pacotes, sinal de codec incompatível. O HLS
            // converte essa mesma faixa para o pipeline de mídia do navegador.
            session.hasAudio = true;
            return { hasAudio: true, audioDeclared: true };
        } catch (error) {
            console.warn("[CFTV HLS] Reprodução compatível indisponível", error?.message ?? String(error));
            hls.destroy();
            if (sessions.get(elementId) === session) sessions.delete(elementId);
            video.removeAttribute("src");
            video.load();
            throw error;
        }
    }

    function waitForHlsAudio(hls, manifest, session, video) {
        const levels = manifest?.levels ?? hls.levels ?? [];
        const declared = levels.some(level => {
            const codec = level.audioCodec ?? level.attrs?.CODECS ?? "";
            return /mp4a|aac|opus|ac-3|ec-3/i.test(String(codec));
        }) || (manifest?.audioTracks?.length ?? hls.audioTracks?.length ?? 0) > 0
            || session.hasAudio;
        if (declared) return Promise.resolve(true);

        return new Promise(resolve => {
            const timeout = setTimeout(() => finish(false), 4500);
            const initialDecodedBytes = video.webkitAudioDecodedByteCount ?? 0;
            const decodedProbe = setInterval(() => {
                if (session.hasAudio || (video.webkitAudioDecodedByteCount ?? 0) > initialDecodedBytes)
                    finish(true);
            }, 180);
            const onCodecs = (_, data) => {
                const tracks = data?.tracks ?? data;
                if (tracks?.audio || tracks?.audiovideo) finish(true);
            };
            const onFragment = () => {
                const current = hls.levels?.[hls.currentLevel];
                if (current?.audioCodec || (hls.audioTracks?.length ?? 0) > 0) finish(true);
            };
            function finish(value) {
                clearTimeout(timeout);
                clearInterval(decodedProbe);
                hls.off(window.Hls.Events.BUFFER_CODECS, onCodecs);
                hls.off(window.Hls.Events.FRAG_BUFFERED, onFragment);
                resolve(value);
            }
            hls.on(window.Hls.Events.BUFFER_CODECS, onCodecs);
            hls.on(window.Hls.Events.FRAG_BUFFERED, onFragment);
        });
    }

    function waitForMedia(video) {
        if (video.readyState >= HTMLMediaElement.HAVE_METADATA) return Promise.resolve();
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => finish(new Error("Tempo limite ao carregar o vídeo.")), 15000);
            function finish(error) {
                clearTimeout(timeout);
                video.removeEventListener("loadedmetadata", loaded);
                video.removeEventListener("error", failed);
                error ? reject(error) : resolve();
            }
            function loaded() { finish(); }
            function failed() { finish(new Error("O navegador recusou o formato de vídeo.")); }
            video.addEventListener("loadedmetadata", loaded);
            video.addEventListener("error", failed);
        });
    }

    async function stop(elementId) {
        const session = sessions.get(elementId); if (!session) return;
        session.audioController?.abort();
        session.audioPeer?.close();
        session.controller?.abort();
        session.peer?.close();
        session.hls?.destroy();
        session.video.pause();
        session.video.muted = true;
        session.video.srcObject = null;
        session.video.removeAttribute("src");
        session.video.load();
        sessions.delete(elementId);
    }

    async function attachAudio(elementId, url) {
        const session = sessions.get(elementId);
        if (!session) return false;

        session.audioController?.abort();
        session.audioPeer?.close();

        const peer = new RTCPeerConnection();
        const controller = new AbortController();
        session.audioPeer = peer;
        session.audioController = controller;
        // Alguns gravadores só negociam a faixa LPCM quando o stream secundário
        // completo é solicitado. O vídeo desta conexão é ignorado; a imagem
        // exibida permanece sendo a do stream principal.
        peer.addTransceiver("video", { direction: "recvonly" });
        peer.addTransceiver("audio", { direction: "recvonly" });
        peer.ontrack = event => {
            if (event.track.kind !== "audio") return;
            for (const current of session.mediaStream.getAudioTracks()) session.mediaStream.removeTrack(current);
            session.mediaStream.addTrack(event.track);
            session.video.srcObject = session.mediaStream;
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
            if (!response.ok || !answer.trim()) throw new Error("O áudio compatível não foi entregue pelo gateway.");
            await peer.setRemoteDescription({ type: "answer", sdp: answer });
            await waitForConnection(peer);
            session.hasAudio = await hasInboundAudio(peer, 3000)
                && session.mediaStream.getAudioTracks().some(track => track.readyState === "live");
            return session.hasAudio;
        } catch (error) {
            controller.abort();
            peer.close();
            if (session.audioPeer === peer) {
                session.audioPeer = null;
                session.audioController = null;
            }
            if (error?.name === "AbortError") return false;
            return false;
        }
    }

    async function setAudioEnabled(elementId, enabled) {
        const session = sessions.get(elementId);
        if (!session) return false;
        const hasAudio = session.hasAudio === true
            || session.mediaStream?.getAudioTracks().some(track => track.readyState === "live");
        if (!hasAudio) return false;
        session.video.muted = !enabled;
        session.video.volume = 1;
        if (enabled) await session.video.play();
        return !session.video.muted;
    }

    async function toggleFullscreen(elementId) {
        const element = document.getElementById(elementId);
        if (!element) throw new Error("A área de monitoramento não foi encontrada.");
        if (document.fullscreenElement || document.webkitFullscreenElement) {
            await exitFullscreen();
            return false;
        }
        const request = element.requestFullscreen ?? element.webkitRequestFullscreen;
        if (!request) throw new Error("A tela cheia não é suportada neste navegador.");
        await request.call(element);
        return true;
    }

    async function exitFullscreen() {
        if (!document.fullscreenElement && !document.webkitFullscreenElement) return;
        const exit = document.exitFullscreen ?? document.webkitExitFullscreen;
        if (exit) await exit.call(document);
    }

    return { start, startHls, attachAudio, stop, setAudioEnabled, toggleFullscreen, exitFullscreen };
})();
