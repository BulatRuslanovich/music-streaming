// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Api.Middleware;

/// <summary>
/// Копит тело ответа в памяти, но только если это JSON и он не слишком велик.
/// </summary>
/// <remarks>
/// Решение принимается на первой записи, а не заранее: Content-Type известен только к этому моменту.
/// Всё остальное — SSE, аудио, обложки — уходит прямо в целевой поток, иначе буфер сожрал бы
/// бесконечный поток событий и держал бы в памяти многомегабайтные файлы.
/// </remarks>
internal sealed class JsonBufferingStream(HttpResponse response, Stream target, int maxBufferedBytes)
    : Stream
{
    private MemoryStream? _buffer;
    private bool _decided;
    private bool _passThrough;

    /// <summary>Накопленное тело, если ответ оказался буферизуемым JSON.</summary>
    public ReadOnlyMemory<byte>? Buffered =>
        _buffer is null ? null : _buffer.GetBuffer().AsMemory(0, (int)_buffer.Length);

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Decide();

        if (_passThrough)
        {
            target.Write(buffer);
            return;
        }

        _buffer!.Write(buffer);
        SpillIfTooLarge();
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Decide();

        if (_passThrough)
        {
            await target.WriteAsync(buffer, cancellationToken);
            return;
        }

        await _buffer!.WriteAsync(buffer, cancellationToken);
        SpillIfTooLarge();
    }

    public override Task WriteAsync(
        byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <summary>Досылает в целевой поток то, что накопилось, если 304-логика не пригодилась.</summary>
    public async Task FlushToTargetAsync(CancellationToken cancellationToken)
    {
        if (_buffer is null || _buffer.Length == 0)
            return;

        var payload = _buffer.GetBuffer().AsMemory(0, (int)_buffer.Length);
        _buffer = null;
        await target.WriteAsync(payload, cancellationToken);
    }

    private void Decide()
    {
        if (_decided)
            return;

        _decided = true;

        var contentType = response.ContentType;
        var buffering = contentType is not null
                        && contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);

        if (buffering)
            _buffer = new MemoryStream();
        else
            _passThrough = true;
    }

    // Ничего ещё не отправлено, поэтому переход в сквозной режим безопасен: сначала досылаем
    // накопленное, дальше пишем напрямую.
    private void SpillIfTooLarge()
    {
        if (_buffer is null || _buffer.Length <= maxBufferedBytes)
            return;

        var payload = _buffer.GetBuffer().AsSpan(0, (int)_buffer.Length);
        target.Write(payload);
        _buffer = null;
        _passThrough = true;
    }

    public override void Flush()
    {
        if (_passThrough)
            target.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _passThrough ? target.FlushAsync(cancellationToken) : Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
