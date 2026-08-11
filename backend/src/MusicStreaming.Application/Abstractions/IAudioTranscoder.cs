namespace MusicStreaming.Application.Abstractions;

public interface IAudioTranscoder
{
    bool IsAvailable { get; }

    Task<bool> TranscodeToOpusAsync(
        string sourceAbsolutePath,
        string targetAbsolutePath,
        int bitrateKbps,
        CancellationToken cancellationToken = default);
}
