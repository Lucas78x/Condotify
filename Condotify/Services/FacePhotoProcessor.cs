using Microsoft.AspNetCore.Components.Forms;

namespace Condotify.Services;

public static class FacePhotoProcessor
{
    public const long MaxOriginalBytes = 5_000_000;
    public const int MaxPreparedBytes = 90_000;

    private static readonly int[] TargetSizes = [720, 600, 480, 360, 280, 200];

    public static bool IsSupported(IBrowserFile file) =>
        file.ContentType is "image/jpeg" or "image/png";

    public static async Task<string> PrepareAsync(IBrowserFile file, CancellationToken cancellationToken = default)
    {
        if (!IsSupported(file))
            throw new InvalidOperationException("Selecione uma foto JPG ou PNG.");
        if (file.Size > MaxOriginalBytes)
            throw new InvalidOperationException("A foto original deve ter no maximo 5 MB.");
        if (file.Size < 5_000)
            throw new InvalidOperationException("A foto possui pouca definicao. Selecione uma imagem mais nitida.");

        foreach (var size in TargetSizes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resized = await file.RequestImageFileAsync("image/jpeg", size, size);
            await using var stream = resized.OpenReadStream(MaxOriginalBytes, cancellationToken);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            if (memory.Length <= MaxPreparedBytes)
                return $"data:image/jpeg;base64,{Convert.ToBase64String(memory.ToArray())}";
        }

        throw new InvalidOperationException("Nao foi possivel reduzir a foto para o limite exigido pelos equipamentos.");
    }
}
