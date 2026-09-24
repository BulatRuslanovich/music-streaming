// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Services;

/// <summary>
/// Очередь на замер громкости трека.
/// </summary>
/// <remarks>
/// Отдельно от <see cref="AudioAnalysisQueue"/> по той же причине, по какой та отделена от
/// эмбеддингов: замер декодирует запись целиком, и ставить его в одну полосу с анализом значило
/// бы, что дешёвый проход ждёт за дорогим. Дозаполнения у неё нет намеренно — громкость нужна
/// ровно тому, что слушают, и гнать ffmpeg по всей библиотеке ради треков, которые никто не
/// включал, на маленькой машине дороже, чем ждать второго прослушивания.
/// </remarks>
public class LoudnessQueue : TrackWorkQueue;
