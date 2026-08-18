using Xunit;

namespace MusicStreaming.IntegrationTests;

internal static class Cancel
{
    public static CancellationToken Token => TestContext.Current.CancellationToken;
}
