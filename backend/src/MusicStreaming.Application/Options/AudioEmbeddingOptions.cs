// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

public class AudioEmbeddingOptions
{
    public const string SectionName = "AudioEmbedding";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Чем считать вектора: <c>clap</c> — настоящей моделью, <c>deterministic</c> — дублем,
    /// раскладывающим путь файла в псевдослучайный вектор. Дубль нужен для локальной разработки
    /// без модели; его вектора о звуке ничего не знают, и в продакшене он бессмыслен.
    /// </summary>
    public string Provider { get; set; } = ClapProvider;

    public const string ClapProvider = "clap";
    public const string DeterministicProvider = "deterministic";

    /// <summary>Путь к .onnx относительно корня хранилища. Файл не в git: это сотни мегабайт.</summary>
    public string ModelPath { get; set; } = "models/clap/audio.onnx";

    /// <summary>
    /// Банк mel-фильтров, выгруженный вместе с моделью. Шкала Slaney не воспроизводится в коде
    /// намеренно: её ручная транскрипция была бы самым вероятным источником тихого расхождения.
    /// </summary>
    public string MelFiltersPath { get; set; } = "models/clap/mel_filters_64x513.f32";

    /// <summary>
    /// Ожидаемый SHA-256 модели. Пусто — проверка не выполняется; заполнено — несовпадение
    /// означает отказ загружать граф. Модель это исполняемый код, а не данные.
    /// </summary>
    public string ModelSha256 { get; set; } = string.Empty;

    /// <summary>
    /// Идентификатор модели. Вместе со стратегией нарезки играет роль версии алгоритма: смена
    /// любого из двух заставляет переэмбеддить всю библиотеку, а это часы или сутки CPU.
    /// Это решение, а не настройка по случаю.
    /// </summary>
    public string ModelId { get; set; } = "laion/larger_clap_music_and_speech";

    public int Dimension { get; set; } = 512;

    /// <summary>Потоков внутри ORT; 0 — четверть ядер, чтобы стриминг не голодал.</summary>
    public int IntraOpThreads { get; set; }

    public int BackfillBatchSize { get; set; } = 4;
    public int PollSeconds { get; set; } = 30;

    public int EffectiveIntraOpThreads =>
        IntraOpThreads > 0 ? IntraOpThreads : Math.Max(1, Environment.ProcessorCount / 4);

    public static OptionsBuilder<AudioEmbeddingOptions> Validated(OptionsBuilder<AudioEmbeddingOptions> builder) => builder
        .Validate(o => o.Provider is ClapProvider or DeterministicProvider,
            $"AudioEmbedding:Provider must be '{ClapProvider}' or '{DeterministicProvider}'.")
        .Validate(o => o.Dimension is >= 32 and <= 4096,
            "AudioEmbedding:Dimension must be between 32 and 4096.")
        .Validate(o => o.IntraOpThreads is >= 0 and <= 128,
            "AudioEmbedding:IntraOpThreads must be between 0 and 128.")
        .Validate(o => o.BackfillBatchSize is >= 1 and <= 64,
            "AudioEmbedding:BackfillBatchSize must be between 1 and 64.")
        .Validate(o => o.PollSeconds is >= 5 and <= 3600,
            "AudioEmbedding:PollSeconds must be between 5 and 3600.")
        .Validate(o => o.ModelSha256.Length is 0 or 64,
            "AudioEmbedding:ModelSha256 must be empty or a 64-character hex digest.")
        .Validate(o => !Path.IsPathRooted(o.ModelPath) && !Path.IsPathRooted(o.MelFiltersPath),
            "AudioEmbedding paths must be relative to Storage:RootPath.");
}
