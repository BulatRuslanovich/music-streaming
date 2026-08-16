using Xunit;

namespace MusicStreaming.IntegrationTests;

/// <summary>
/// Токен отмены текущего теста.
///
/// <para>
/// Прокси к <c>Cancel.Token</c> ради длины строки: он нужен последним
/// аргументом почти каждому обращению к базе и к API, а в исходном виде занимает больше места,
/// чем сам вызов, и разгоняет строки за все разумные пределы.
/// </para>
/// </summary>
internal static class Cancel
{
    public static CancellationToken Token => TestContext.Current.CancellationToken;
}
