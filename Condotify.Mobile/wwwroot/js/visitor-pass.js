(function () {
    "use strict";

    const loadImage = (source) => new Promise((resolve, reject) => {
        const image = new Image();
        image.onload = () => resolve(image);
        image.onerror = () => reject(new Error("Não foi possível carregar o QR Code."));
        image.src = source;
    });

    const roundedRect = (context, x, y, width, height, radius) => {
        const safeRadius = Math.min(radius, width / 2, height / 2);
        context.beginPath();
        context.moveTo(x + safeRadius, y);
        context.lineTo(x + width - safeRadius, y);
        context.quadraticCurveTo(x + width, y, x + width, y + safeRadius);
        context.lineTo(x + width, y + height - safeRadius);
        context.quadraticCurveTo(x + width, y + height, x + width - safeRadius, y + height);
        context.lineTo(x + safeRadius, y + height);
        context.quadraticCurveTo(x, y + height, x, y + height - safeRadius);
        context.lineTo(x, y + safeRadius);
        context.quadraticCurveTo(x, y, x + safeRadius, y);
        context.closePath();
    };

    const fillRoundedRect = (context, x, y, width, height, radius, fill) => {
        roundedRect(context, x, y, width, height, radius);
        context.fillStyle = fill;
        context.fill();
    };

    const fitText = (context, text, maxWidth, preferredSize, minimumSize, weight) => {
        const value = (text || "").trim();
        let size = preferredSize;
        do {
            context.font = `${weight} ${size}px Inter, Arial, sans-serif`;
            if (context.measureText(value).width <= maxWidth) return size;
            size -= 2;
        } while (size > minimumSize);
        return minimumSize;
    };

    const truncate = (context, text, maxWidth) => {
        const value = (text || "").trim();
        if (context.measureText(value).width <= maxWidth) return value;
        let shortened = value;
        while (shortened.length > 1 && context.measureText(`${shortened}…`).width > maxWidth) {
            shortened = shortened.slice(0, -1);
        }
        return `${shortened}…`;
    };

    const drawBrandMark = (context, x, y) => {
        fillRoundedRect(context, x, y, 86, 86, 25, "#ffffff");
        context.fillStyle = "#092557";
        context.font = "900 25px Inter, Arial, sans-serif";
        context.textAlign = "center";
        context.fillText("F&F", x + 43, y + 50);
        context.fillStyle = "#7bc053";
        context.fillRect(x + 25, y + 61, 36, 6);
        context.textAlign = "start";
    };

    const drawCalendar = (context, x, y) => {
        context.strokeStyle = "#7bc053";
        context.lineWidth = 4;
        roundedRect(context, x, y + 4, 34, 31, 7);
        context.stroke();
        context.beginPath();
        context.moveTo(x + 2, y + 14);
        context.lineTo(x + 32, y + 14);
        context.moveTo(x + 10, y);
        context.lineTo(x + 10, y + 9);
        context.moveTo(x + 24, y);
        context.lineTo(x + 24, y + 9);
        context.stroke();
    };

    const drawAccessCard = async (model) => {
        if (!model || !model.qrDataUri) throw new Error("Os dados do convite estão incompletos.");
        if (document.fonts && document.fonts.ready) await document.fonts.ready;

        const canvas = document.createElement("canvas");
        canvas.width = 1080;
        canvas.height = 1440;
        const context = canvas.getContext("2d");
        const qrCode = await loadImage(model.qrDataUri);

        context.fillStyle = "#eef3fb";
        context.fillRect(0, 0, canvas.width, canvas.height);

        const headerGradient = context.createLinearGradient(0, 0, canvas.width, 350);
        headerGradient.addColorStop(0, "#061a3d");
        headerGradient.addColorStop(0.55, "#092557");
        headerGradient.addColorStop(1, "#123d79");
        context.fillStyle = headerGradient;
        context.fillRect(0, 0, canvas.width, 345);

        context.globalAlpha = 0.11;
        context.fillStyle = "#ffffff";
        context.beginPath();
        context.arc(965, 40, 215, 0, Math.PI * 2);
        context.fill();
        context.beginPath();
        context.arc(80, 330, 150, 0, Math.PI * 2);
        context.fill();
        context.globalAlpha = 1;

        drawBrandMark(context, 72, 68);
        context.fillStyle = "#ffffff";
        context.font = "800 42px Inter, Arial, sans-serif";
        context.fillText("F&F Access", 180, 112);
        context.fillStyle = "rgba(255,255,255,.72)";
        context.font = "700 19px Inter, Arial, sans-serif";
        context.letterSpacing = "3px";
        context.fillText("CONVITE DE ACESSO", 180, 145);
        context.letterSpacing = "0px";

        context.fillStyle = "#ffffff";
        context.font = "800 48px Inter, Arial, sans-serif";
        context.fillText("Seu acesso está pronto", 72, 246);
        context.fillStyle = "rgba(255,255,255,.78)";
        context.font = "500 25px Inter, Arial, sans-serif";
        context.fillText("Apresente este QR Code na portaria", 72, 287);

        context.save();
        context.shadowColor = "rgba(18, 39, 83, .16)";
        context.shadowBlur = 40;
        context.shadowOffsetY = 18;
        fillRoundedRect(context, 54, 306, 972, 1070, 50, "#ffffff");
        context.restore();

        fillRoundedRect(context, 705, 350, 267, 54, 27, "#e8f8f2");
        context.fillStyle = "#087e68";
        context.beginPath();
        context.arc(737, 377, 8, 0, Math.PI * 2);
        context.fill();
        context.font = "800 16px Inter, Arial, sans-serif";
        context.fillText("ACESSO TEMPORÁRIO", 759, 383);

        context.fillStyle = "#748198";
        context.font = "800 18px Inter, Arial, sans-serif";
        context.letterSpacing = "3px";
        context.fillText("VISITANTE", 108, 383);
        context.letterSpacing = "0px";
        const nameSize = fitText(context, model.visitorName, 620, 52, 30, 800);
        context.fillStyle = "#172238";
        context.font = `800 ${nameSize}px Inter, Arial, sans-serif`;
        context.fillText(truncate(context, model.visitorName, 620), 108, 442);
        context.fillStyle = "#60708a";
        context.font = "500 26px Inter, Arial, sans-serif";
        context.fillText(truncate(context, model.location, 620), 108, 482);

        context.strokeStyle = "#e3e9f3";
        context.lineWidth = 2;
        context.beginPath();
        context.moveTo(108, 520);
        context.lineTo(972, 520);
        context.stroke();

        context.save();
        context.shadowColor = "rgba(20, 45, 90, .12)";
        context.shadowBlur = 28;
        context.shadowOffsetY = 12;
        fillRoundedRect(context, 242, 565, 596, 596, 42, "#ffffff");
        context.restore();
        context.strokeStyle = "#dce5f4";
        context.lineWidth = 3;
        roundedRect(context, 242, 565, 596, 596, 42);
        context.stroke();
        context.drawImage(qrCode, 282, 605, 516, 516);

        fillRoundedRect(context, 343, 1133, 394, 52, 26, "#eef3ff");
        context.fillStyle = "#092557";
        context.font = "700 21px Inter, Arial, sans-serif";
        context.textAlign = "center";
        context.fillText("APONTE A CÂMERA PARA O QR CODE", 540, 1167);
        context.textAlign = "start";

        const boxY = 1210;
        fillRoundedRect(context, 108, boxY, 418, 105, 22, "#f5f7fb");
        fillRoundedRect(context, 554, boxY, 418, 105, 22, "#f5f7fb");
        drawCalendar(context, 137, boxY + 31);
        drawCalendar(context, 583, boxY + 31);

        context.fillStyle = "#77849a";
        context.font = "800 15px Inter, Arial, sans-serif";
        context.fillText("ENTRADA A PARTIR DE", 189, boxY + 39);
        context.fillText("VÁLIDO ATÉ", 635, boxY + 39);
        context.fillStyle = "#1d2940";
        context.font = "800 24px Inter, Arial, sans-serif";
        context.fillText(model.validFrom, 189, boxY + 75);
        context.fillText(model.validTo, 635, boxY + 75);

        context.fillStyle = "#748198";
        context.font = "600 18px Inter, Arial, sans-serif";
        context.textAlign = "center";
        context.fillText("Convite pessoal e protegido • Validação automática na entrada", 540, 1352);
        context.textAlign = "start";

        return canvas.toDataURL("image/png");
    };

    window.condotifyVisitorPass = { render: drawAccessCard };
})();
