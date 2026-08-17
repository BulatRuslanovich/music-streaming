using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Api.Startup;

public static class LimitsSetup
{
    private const long MultipartOverhead = 1024 * 1024;

    private const int MaxFormValueLength = 64 * 1024;

    /// <summary>
    /// Границы одного запроса на загрузку.
    ///
    /// <para>
    /// Проверка размера в <c>TrackUploadService</c> одна на всех, но срабатывает она поздно:
    /// привязка <c>IFormFileCollection</c> разбирает multipart до входа в действие, а всё крупнее
    /// нескольких десятков килобайт ASP.NET Core к этому моменту уже сбросил во временный файл.
    /// То есть отвергнутый запрос успевает занять на диске ровно столько, сколько прислали, —
    /// и единственное, что этому мешает, находится здесь.
    /// </para>
    ///
    /// <para>
    /// Потолок один на весь запрос, а не на файл: пакет из нескольких файлов эндпоинт по-прежнему
    /// принимает, но суммарно не больше, чем весит один допустимый файл. Клиент шлёт по одному
    /// файлу на запрос (см. <c>uploadOneFile</c>), так что настоящей загрузки это не касается.
    /// </para>
    /// </summary>
    public static WebApplicationBuilder AddApiUploadLimits(this WebApplicationBuilder builder)
    {
        var storage = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
                      ?? new StorageOptions();

        var ceiling = storage.MaxUploadBytes + MultipartOverhead;

        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = ceiling;
            options.ValueLengthLimit = MaxFormValueLength;
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = ceiling;
            options.Limits.MinRequestBodyDataRate = null;
        });

        builder.Services
            .AddDataProtection()
            .SetApplicationName("music-streaming")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(storage.RootPath, ".dataprotection")));

        return builder;
    }
}
