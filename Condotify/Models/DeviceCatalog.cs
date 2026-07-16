namespace Condotify.Models;

public sealed record AccessDeviceOption(int Type, string Brand, string Model, string Category);

public static class DeviceCatalog
{
    private static readonly string[] ApiModels =
    [
        "SS5520", "SS5530MFFace", "SS5530MFFaceLite", "SS3530MFFaceW", "SS3430Bio", "SS3430MFBio",
        "SS7520FaceT", "SS7530Face", "SS3530MFFace", "SS3540MFFaceEx", "SS1540MFW", "SS1530MFW",
        "SS3540MFFaceBioEx", "SS3540MFFaceBio", "SS3532MFW", "SS3532MF", "SS3542MFW", "SS3531MF",
        "SS5531MFW", "SS5541MFW", "SS5532MFW", "SS5542MFW", "SS5430MFBioFT", "SS3541MF",
        "CT30002PB", "CT30004PB", "SS3710UHF", "IdFace", "IdFaceMax", "ControlIdUHF"
    ];

    public static IReadOnlyList<AccessDeviceOption> AccessDevices { get; } =
    [
        new(0, "Intelbras", "SS 5520", "Reconhecimento facial"),
        new(1, "Intelbras", "SS 5530 MF FACE", "Reconhecimento facial"),
        new(2, "Intelbras", "SS 5530 MF FACE LITE", "Reconhecimento facial"),
        new(3, "Intelbras", "SS 3530 MF FACE W", "Reconhecimento facial"),
        new(4, "Intelbras", "SS 3430 BIO", "Biometria"),
        new(5, "Intelbras", "SS 3430 MF BIO", "Biometria"),
        new(6, "Intelbras", "SS 7520 FACE T", "Reconhecimento facial"),
        new(7, "Intelbras", "SS 7530 FACE", "Reconhecimento facial"),
        new(8, "Intelbras", "SS 3530 MF FACE", "Reconhecimento facial"),
        new(9, "Intelbras", "SS 3540 MF FACE EX", "Reconhecimento facial"),
        new(10, "Intelbras", "SS 1540 MF W", "Cartao e senha"),
        new(11, "Intelbras", "SS 1530 MF W", "Cartao e senha"),
        new(12, "Intelbras", "SS 3540 MF FACE BIO EX", "Face e biometria"),
        new(13, "Intelbras", "SS 3540 MF FACE BIO", "Face e biometria"),
        new(14, "Intelbras", "SS 3532 MF W", "Cartao e senha"),
        new(15, "Intelbras", "SS 3532 MF", "Cartao e senha"),
        new(16, "Intelbras", "SS 3542 MF W", "Cartao e senha"),
        new(17, "Intelbras", "SS 3531 MF", "Cartao e senha"),
        new(18, "Intelbras", "SS 5531 MF W", "Reconhecimento facial"),
        new(19, "Intelbras", "SS 5541 MF W", "Reconhecimento facial"),
        new(20, "Intelbras", "SS 5532 MF W", "Reconhecimento facial"),
        new(21, "Intelbras", "SS 5542 MF W", "Reconhecimento facial"),
        new(22, "Intelbras", "SS 5430 MF BIO FT", "Face e biometria"),
        new(23, "Intelbras", "SS 3541 MF", "Cartao e senha"),
        new(24, "Intelbras", "CT 3000 2PB", "Controladora"),
        new(25, "Intelbras", "CT 3000 4PB", "Controladora"),
        new(26, "Intelbras", "SS 3710 UHF", "Leitor veicular UHF"),
        new(27, "Control iD", "iDFace", "Reconhecimento facial"),
        new(28, "Control iD", "iDFace Max", "Reconhecimento facial"),
        new(29, "Control iD", "iDUHF", "Leitor veicular UHF")
    ];

    public static AccessDeviceOption? Find(int type) => AccessDevices.FirstOrDefault(x => x.Type == type);
    public static string ApiModel(int type) => type >= 0 && type < ApiModels.Length ? ApiModels[type] : string.Empty;
}
