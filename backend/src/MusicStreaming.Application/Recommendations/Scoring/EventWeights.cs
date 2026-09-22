// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations.Scoring;

/// <summary>How much one playback event moves a scalar affinity.</summary>
/// <remarks>
/// Единица шкалы — одно полное прослушивание. Всё остальное названо относительно него, и читать
/// таблицу надо так: «лайк стоит двух с половиной дослушиваний».
/// <para>
/// Веса намеренно несимметричны. Явный жест (лайк, плейлист) весит больше неявного, потому что
/// он стоил человеку действия. Снятие жеста (<see cref="UnlikedWeight"/>) бьёт ровно настолько
/// же, насколько давал лайк, — передумать должно стирать, а не оставлять половину. А вот удаление
/// из плейлиста мягче добавления: плейлисты чистят от разонравившегося, но и просто от
/// разросшегося, так что это слабее заявление, чем «мне это не нравится».
/// </para>
/// <para>
/// Это <b>скалярный</b> реестр: веса складываются и затухают месяцами через
/// <see cref="RecencyDecay"/>. Векторный вкус считает те же события иначе — см.
/// <see cref="TasteSignal"/>, где объяснено, почему две шкалы, а не одна.
/// </para>
/// </remarks>
public static class EventWeights
{
    // Пропуск: вес зависит от того, сколько успели послушать. Ступени, а не непрерывная функция,
    // потому что пороги здесь качественные — «не начал», «не зашло», «слушал, но ушёл».
    public const double AbandonedWeight = -1.0;
    public const double DroppedWeight = -0.5;
    public const double PartialWeight = -0.1;
    public const double SustainedWeight = 0.3;
    public const double NearCompleteWeight = 0.8;

    public const double CompletedWeight = 1.0;
    public const double ReplayedWeight = 0.8;
    public const double LikedWeight = 2.5;
    public const double UnlikedWeight = -2.5;
    public const double PlaylistAddWeight = 2.0;
    public const double PlaylistRemoveWeight = -1.5;
    public const double QueueAddWeight = 0.8;

    /// <summary>Открытие страницы артиста или жанра: интерес, но ещё не выбор.</summary>
    public const double EntityInterestWeight = 0.2;

    /// <summary>
    /// Доля прослушанного в вес. Пороги качественные: ниже 5 % трек, считай, не начинали
    /// (промотали мимо), ниже 20 % — не зашёл, выше 80 % — дослушали и ушли на титрах.
    /// </summary>
    public static double ForCompletion(double ratio) => ratio switch
    {
        < 0.05 => AbandonedWeight,
        < 0.20 => DroppedWeight,
        < 0.50 => PartialWeight,
        < 0.80 => SustainedWeight,
        _ => NearCompleteWeight,
    };

    public static double ForTrack(PlaybackEventType type, double completionRatio) => type switch
    {
        PlaybackEventType.TrackSkipped => ForCompletion(completionRatio),
        PlaybackEventType.TrackCompleted => CompletedWeight,
        PlaybackEventType.TrackReplayed => ReplayedWeight,
        PlaybackEventType.TrackLiked => LikedWeight,
        PlaybackEventType.TrackUnliked => UnlikedWeight,
        PlaybackEventType.TrackAddedToPlaylist => PlaylistAddWeight,
        PlaybackEventType.TrackRemovedFromPlaylist => PlaylistRemoveWeight,
        PlaybackEventType.TrackAddedToQueue => QueueAddWeight,

        PlaybackEventType.SearchResultClicked => EntityInterestWeight,

        PlaybackEventType.TrackStarted => 0,
        PlaybackEventType.TrackPlayed => 0,
        PlaybackEventType.TrackPaused => 0,

        _ => 0,
    };

    // Только те открытия, которые роллап умеет привязать к артисту. У PlaylistOpened и
    // SearchResultClicked цель может быть чем угодно, поэтому веса за них здесь нет.
    public static double ForEntity(PlaybackEventType type) => type switch
    {
        PlaybackEventType.ArtistOpened => EntityInterestWeight,
        PlaybackEventType.AlbumOpened => EntityInterestWeight,
        _ => 0,
    };

    public static bool IsSkip(PlaybackEventType type, double completionRatio) =>
        type == PlaybackEventType.TrackSkipped && completionRatio < 0.20;

    public static bool ShouldRefreshRecommendations(PlaybackEventType type, double completionRatio) => type switch
    {
        PlaybackEventType.TrackCompleted => true,
        PlaybackEventType.TrackReplayed => true,
        PlaybackEventType.TrackLiked => true,
        PlaybackEventType.TrackUnliked => true,
        PlaybackEventType.TrackAddedToPlaylist => true,
        PlaybackEventType.TrackRemovedFromPlaylist => true,
        PlaybackEventType.TrackAddedToQueue => true,
        PlaybackEventType.TrackSkipped => completionRatio < 0.20 || completionRatio >= 0.80,
        _ => false,
    };

    public static double CompletionRatio(int listenedSeconds, int durationSeconds)
    {
        if (durationSeconds <= 0 || listenedSeconds <= 0)
            return 0;

        return Math.Min(1.0, (double)listenedSeconds / durationSeconds);
    }
}
