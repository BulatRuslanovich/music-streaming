// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { listeningChange, type MonthlyRecap } from "@/lib/recap";
import type { StatisticsEntry, StatisticsTrack } from "@/lib/types";

/**
 * Состав истории итогов.
 *
 * Решение о том, из чего складывается месяц, отделено от разметки: у тощего месяца половины
 * слайдов быть не должно, и проверять это дешевле здесь, чем гонять пустой экран в браузере.
 */
export type RecapSlide =
  | { kind: "intro" }
  | { kind: "time"; changePercent: number | null }
  | { kind: "topTrack"; entry: StatisticsTrack }
  | { kind: "topTracks"; entries: StatisticsTrack[] }
  | { kind: "topArtist"; entry: StatisticsEntry }
  | { kind: "discoveries"; entries: StatisticsEntry[] }
  | { kind: "genre"; genre: string; previous: string | null }
  | { kind: "finale" };

export type RecapSlideKind = RecapSlide["kind"];

/** Сколько треков показывает слайд со списком. */
export const STORY_TRACK_COUNT = 5;

/** Сколько открытий помещается в строку, не превращая слайд в перечень. */
export const STORY_DISCOVERY_COUNT = 5;

export function recapSlides(data: MonthlyRecap): RecapSlide[] {
  const [leader, ...rest] = data.topTracks;
  const artist = data.topArtists[0];
  const discoveries = data.discoveries.slice(0, STORY_DISCOVERY_COUNT);

  return [
    { kind: "intro" },
    {
      kind: "time",
      changePercent: listeningChange(data.listenedSeconds, data.previousListenedSeconds),
    },
    ...(leader ? [{ kind: "topTrack", entry: leader } as const] : []),
    // Список нужен, только если за лидером что-то стоит: иначе он повторяет предыдущий слайд.
    ...(leader && rest.length > 0
      ? [{ kind: "topTracks", entries: data.topTracks.slice(0, STORY_TRACK_COUNT) } as const]
      : []),
    ...(artist ? [{ kind: "topArtist", entry: artist } as const] : []),
    ...(discoveries.length > 0 ? [{ kind: "discoveries", entries: discoveries } as const] : []),
    ...(data.topGenre
      ? [
          {
            kind: "genre",
            genre: data.topGenre,
            // Прошлый жанр доезжает не всегда — поле необязательное, и «сменился с ничего»
            // писать нельзя. Сюда попадает только настоящая смена.
            previous:
              data.previousTopGenre && data.previousTopGenre !== data.topGenre
                ? data.previousTopGenre
                : null,
          } as const,
        ]
      : []),
    { kind: "finale" },
  ];
}
