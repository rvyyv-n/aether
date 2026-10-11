namespace Aether.PvP.Review.Clipping;

public static class LosslessClipper
{
    public static async Task<string> ExportLosslessAsync(
        ClipRequest request, string ffmpegPath = "ffmpeg.exe", CancellationToken cancellationToken = default)
    {
        using var service = new ClipExportService(ffmpegPath);
        var result = await service.ExportAsync(request, cancellationToken).ConfigureAwait(false);
        return result.OutputPath;
    }
}
