// Mesmo padrao de Condotify/wwwroot/js/portal-interop.js (downloadBase64).
// window.open("data:...") nao funciona: navegadores modernos bloqueiam navegacao
// de topo para URLs data:, e na WebView do MAUI (Android/iOS) window.open e um
// no-op sem plumbing nativo. Blob + createObjectURL + clique em <a download> e o
// caminho que funciona nos dois.
window.condotifyFileDownload = {
    downloadBase64: function (fileName, contentBase64, contentType) {
        const binary = atob(contentBase64);
        const bytes = new Uint8Array(binary.length);
        for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
        const blob = new Blob([bytes], { type: contentType || "application/octet-stream" });
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = fileName;
        anchor.rel = "noopener";
        document.body.appendChild(anchor);
        anchor.click();
        document.body.removeChild(anchor);
        URL.revokeObjectURL(url);
    }
};
