namespace MusicStreaming.Application.Services;

/// <summary>
/// Кто из устройств пользователя играет прямо сейчас — не более одного.
///
/// <para>
/// Держится в памяти процесса и намеренно не в базе. Воспроизведение эфемерно: пережившая
/// перезапуск запись «играет устройство X» заперла бы музыку в пользу устройства, которого давно
/// нет, и снять эту блокировку человеку было бы нечем. После перезапуска никто не играет — и это
/// ровно то состояние, из которого любое устройство честно начинает заново.
/// </para>
///
/// <para>
/// Право играть держит само соединение, а не запись о нём: устройство подписывается на поток
/// событий и тем самым заявляет «играю я». Оборвалось соединение — право освободилось само, без
/// таймаутов и уборщиков. Упавшую вкладку не нужно отличать от закрытой.
/// </para>
/// </summary>
public sealed class PlaybackSessionRegistry
{
    // Обычный словарь под замком, а не ConcurrentDictionary: обе операции читают прежнее значение и
    // тут же его заменяют, а атомарного «отдай прежнее и положи новое» у ConcurrentDictionary нет —
    // AddOrUpdate возвращает как раз новое. Спорят здесь несколько устройств одного человека, так
    // что цена замка неразличима, а правильность видна глазом.
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, PlaybackHolder> holders = [];

    /// <summary>
    /// Отдаёт право играть названному устройству, отбирая его у прежнего.
    /// </summary>
    /// <param name="userId">Чьё воспроизведение.</param>
    /// <param name="deviceId">Устройство, которое начинает играть.</param>
    /// <returns>Держатель права; его нужно вернуть через <see cref="Release" />, когда соединение закончится.</returns>
    public PlaybackHolder Claim(Guid userId, string deviceId)
    {
        var holder = new PlaybackHolder(deviceId);
        PlaybackHolder? previous;

        lock (gate)
        {
            holders.TryGetValue(userId, out previous);
            holders[userId] = holder;
        }

        // То же устройство, переподключившееся после обрыва, не вытесняет само себя: иначе сеть,
        // моргнувшая на секунду, показала бы человеку «играет на другом устройстве» — про него же.
        if (previous is not null && previous.DeviceId != deviceId)
            previous.Displace(deviceId);

        return holder;
    }

    /// <summary>
    /// Снимает право, если его всё ещё держит именно этот <paramref name="holder" />.
    ///
    /// <para>
    /// Сверка обязательна: закрывающееся соединение может быть уже вытесненным, и снимать чужое,
    /// только что выданное право оно не должно.
    /// </para>
    /// </summary>
    public void Release(Guid userId, PlaybackHolder holder)
    {
        lock (gate)
        {
            if (holders.TryGetValue(userId, out var current) && ReferenceEquals(current, holder))
                holders.Remove(userId);
        }
    }
}

/// <summary>Одно живое соединение, держащее право играть.</summary>
public sealed class PlaybackHolder(string deviceId)
{
    private readonly TaskCompletionSource displaced = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string DeviceId { get; } = deviceId;

    /// <summary>Устройство, забравшее право. Заполняется в момент вытеснения.</summary>
    public string? DisplacedBy { get; private set; }

    internal void Displace(string byDeviceId)
    {
        DisplacedBy = byDeviceId;
        displaced.TrySetResult();
    }

    /// <summary>
    /// Ждёт вытеснения, но не дольше <paramref name="within" />.
    /// </summary>
    /// <returns>
    /// <c>true</c>, если право отобрали. <c>false</c>, если истёк срок ожидания или ушёл сам
    /// клиент, — в обоих случаях решение, что делать дальше, принимает вызывающий.
    /// </returns>
    public async Task<bool> WasDisplacedAsync(TimeSpan within, CancellationToken ct)
    {
        try
        {
            await displaced.Task.WaitAsync(within, ct);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
