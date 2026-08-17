using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CondotifyAPI.Domain.DTO.Operations;

namespace CondotifyAPI.Services.Operations;

public interface IAppleWalletPassService
{
    Task<bool> IsConfiguredAsync(Guid enterpriseId, CancellationToken cancellationToken = default);
    Task<byte[]> BuildAsync(DigitalPassDTO pass, CancellationToken cancellationToken = default);
}

public sealed class AppleWalletPassService(IWalletIntegrationStore integrationStore) : IAppleWalletPassService
{
    public async Task<bool> IsConfiguredAsync(Guid enterpriseId, CancellationToken cancellationToken = default) =>
        await integrationStore.GetAppleAsync(enterpriseId, cancellationToken) is not null;

    public async Task<byte[]> BuildAsync(DigitalPassDTO pass, CancellationToken cancellationToken = default)
    {
        var settings = await integrationStore.GetAppleAsync(pass.License?.EnterpriseId ?? Guid.Empty, cancellationToken)
            ?? throw new InvalidOperationException("A assinatura do Apple Wallet nao esta configurada.");
        var visit = pass.Visit;
        var unit = visit.HostResident?.Unit;
        var destination = unit is null ? string.Empty : $"{unit.Block?.Name} / {unit.Number}".Trim(' ', '/');
        var passJson = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["formatVersion"] = 1,
            ["passTypeIdentifier"] = settings.PassTypeIdentifier,
            ["serialNumber"] = pass.Id.ToString("N"),
            ["teamIdentifier"] = settings.TeamIdentifier,
            ["organizationName"] = "Condotify",
            ["description"] = $"Autorizacao de acesso para {visit.VisitorName}",
            ["logoText"] = "Condotify",
            ["foregroundColor"] = "rgb(255, 255, 255)",
            ["backgroundColor"] = "rgb(23, 62, 156)",
            ["labelColor"] = "rgb(198, 216, 255)",
            ["expirationDate"] = visit.ValidTo.ToUniversalTime().ToString("O"),
            ["voided"] = pass.Status != DigitalPassStatusEnum.Active,
            ["generic"] = new Dictionary<string, object>
            {
                ["primaryFields"] = new object[] { Field("visitor", "VISITANTE", visit.VisitorName) },
                ["secondaryFields"] = new object[]
                {
                    Field("host", "ANFITRIAO", visit.HostResident?.Name ?? string.Empty),
                    Field("destination", "DESTINO", destination)
                },
                ["auxiliaryFields"] = new object[]
                {
                    Field("validFrom", "VALIDO DE", visit.ValidFrom.ToLocalTime().ToString("dd/MM HH:mm")),
                    Field("validTo", "VALIDO ATE", visit.ValidTo.ToLocalTime().ToString("dd/MM HH:mm"))
                },
                ["backFields"] = new object[]
                {
                    Field("purpose", "MOTIVO", string.IsNullOrWhiteSpace(visit.Purpose) ? "Visita" : visit.Purpose),
                    Field("security", "SEGURANCA", "Passe pessoal e temporario. Nao compartilhe fora da visita.")
                }
            },
            ["barcodes"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["format"] = "PKBarcodeFormatQR",
                    ["message"] = visit.Credential?.Identifier ?? string.Empty,
                    ["messageEncoding"] = "iso-8859-1",
                    ["altText"] = visit.Credential?.Identifier ?? string.Empty
                }
            }
        });

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["pass.json"] = passJson,
            ["icon.png"] = SolidPng(29, 29, 23, 62, 156),
            ["icon@2x.png"] = SolidPng(58, 58, 23, 62, 156)
        };
        var manifest = files.ToDictionary(
            item => item.Key,
            item => Convert.ToHexString(SHA1.HashData(item.Value)).ToLowerInvariant(),
            StringComparer.Ordinal);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        files["manifest.json"] = manifestBytes;
        files["signature"] = Sign(manifestBytes, settings);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(file.Value);
            }
        }
        return output.ToArray();
    }

    private static byte[] Sign(byte[] manifest, AppleWalletSettings settings)
    {
        var certificate = new X509Certificate2(
            Convert.FromBase64String(settings.CertificateBase64),
            settings.CertificatePassword,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
        if (!certificate.HasPrivateKey) throw new CryptographicException("O certificado do passe nao possui chave privada.");
        using var wwdr = LoadCertificate(settings.WwdrCertificate);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate)
        {
            IncludeOption = X509IncludeOption.EndCertOnly,
            DigestAlgorithm = new Oid("1.3.14.3.2.26")
        };
        signer.Certificates.Add(wwdr);
        var cms = new SignedCms(new ContentInfo(manifest), detached: true);
        cms.ComputeSignature(signer);
        certificate.Dispose();
        return cms.Encode();
    }

    private static X509Certificate2 LoadCertificate(string value) => value.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal)
        ? X509Certificate2.CreateFromPem(value.Replace("\\n", "\n", StringComparison.Ordinal))
        : new X509Certificate2(Convert.FromBase64String(value));

    private static Dictionary<string, object> Field(string key, string label, string value) => new()
    { ["key"] = key, ["label"] = label, ["value"] = value };

    private static byte[] SolidPng(int width, int height, byte red, byte green, byte blue)
    {
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < width; x++) raw.Write([red, green, blue, (byte)255]);
        }
        raw.Position = 0;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) raw.CopyTo(zlib);
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(header.Slice(4, 4), height);
        header[8] = 8; header[9] = 6;
        WriteChunk(png, "IHDR", header.ToArray());
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes); stream.Write(data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, Crc32(typeBytes.Concat(data).ToArray()));
        stream.Write(crcBytes);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xffffffffu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }
        return ~crc;
    }
}
