using MusicStreaming.Application.Services;
using Xunit;

namespace MusicStreaming.UnitTests;

/// <summary>
/// Правило «играет одно устройство» целиком живёт здесь, поэтому и проверяется здесь: реестр ни от
/// чего не зависит, а поверх него лежит только перекладывание событий в поток.
/// </summary>
public class PlaybackSessionRegistryTests
{
    /// <summary>Ожидание, заведомо большее любой задержки внутри теста, но не вешающее прогон.</summary>
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(5);

    /// <summary>Ожидание, которое должно истечь: проверяет, что вытеснения не случилось.</summary>
    private static readonly TimeSpan Blink = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task A_second_device_takes_playback_from_the_first()
    {
        var registry = new PlaybackSessionRegistry();
        var user = Guid.CreateVersion7();

        var phone = registry.Claim(user, "phone");
        registry.Claim(user, "laptop");

        Assert.True(await phone.WasDisplacedAsync(Soon, CancellationToken.None));
        Assert.Equal("laptop", phone.DisplacedBy);
    }

    /// <summary>
    /// Моргнувшая сеть не должна выглядеть как второе устройство: переподключившись, та же вкладка
    /// обязана получить право молча, иначе человеку сообщат, что его вытеснил он сам.
    /// </summary>
    [Fact]
    public async Task A_device_reconnecting_under_the_same_name_does_not_displace_itself()
    {
        var registry = new PlaybackSessionRegistry();
        var user = Guid.CreateVersion7();

        var first = registry.Claim(user, "phone");
        registry.Claim(user, "phone");

        Assert.False(await first.WasDisplacedAsync(Blink, CancellationToken.None));
        Assert.Null(first.DisplacedBy);
    }

    /// <summary>
    /// Вытесненное соединение закрывается последним и убирает за собой — но убрать оно должно
    /// только своё. Снимись оно с чужого, только что выданного права, следующее устройство никого
    /// бы не вытеснило.
    /// </summary>
    [Fact]
    public async Task A_displaced_holder_cleaning_up_does_not_free_the_new_one()
    {
        var registry = new PlaybackSessionRegistry();
        var user = Guid.CreateVersion7();

        var phone = registry.Claim(user, "phone");
        var laptop = registry.Claim(user, "laptop");

        // Телефон узнал о вытеснении и закрывает соединение уже после того, как право ушло.
        registry.Release(user, phone);

        registry.Claim(user, "tablet");

        Assert.True(await laptop.WasDisplacedAsync(Soon, CancellationToken.None));
        Assert.Equal("tablet", laptop.DisplacedBy);
    }

    [Fact]
    public async Task Devices_of_different_people_do_not_displace_each_other()
    {
        var registry = new PlaybackSessionRegistry();

        var hers = registry.Claim(Guid.CreateVersion7(), "phone");
        registry.Claim(Guid.CreateVersion7(), "laptop");

        Assert.False(await hers.WasDisplacedAsync(Blink, CancellationToken.None));
    }

    /// <summary>Ушедший клиент — не вытеснение: ждать дальше нечего, но и паузу объявлять некому.</summary>
    [Fact]
    public async Task A_cancelled_wait_is_not_reported_as_a_displacement()
    {
        var registry = new PlaybackSessionRegistry();
        var holder = registry.Claim(Guid.CreateVersion7(), "phone");

        using var gone = new CancellationTokenSource();
        await gone.CancelAsync();

        Assert.False(await holder.WasDisplacedAsync(Soon, gone.Token));
    }
}
