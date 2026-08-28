// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { playableTier } from "@/lib/audioFormats";
import { adaptiveCooldownMs, decideRecovery, type Recovery } from "@/lib/streamRecovery";
import type { AudioQuality, Track } from "@/lib/types";

/**
 * Состояние восстановления воспроизведения: попытки, откаты по качеству, деградация под
 * медленную сеть и память об оборванном источнике.
 *
 * Раньше это жило шестью `useRef` внутри `usePlaybackEngine`, связанными неявными
 * инвариантами, а центральный эффект имел четырнадцать зависимостей — и любое изменение
 * любой из них могло пересобрать источник, то есть оборвать звук. Здесь всё это — обычный
 * объект с явными переходами, как у `AdaptivePlayback` рядом.
 *
 * Решение «что делать с ошибкой» по-прежнему принимает чистая `decideRecovery`; класс
 * только хранит то, на что она опирается, и запоминает её последствия.
 */
export class PlaybackRecovery {
  private retry: { trackId: string; tier: AudioQuality; attempts: number } = {
    trackId: "",
    tier: "Original",
    attempts: 0,
  };

  /** Оборванный источник: `<audio>` остался с мёртвым src и сам не оживёт. */
  private failed: { trackId: string; resume: boolean } | null = null;

  /** Треки, для которых оригинал не проигрался и мы ушли на перекодированную ступень. */
  private readonly fellBack = new Set<string>();

  private degradedUntil = 0;

  private degradations = 0;

  /** Трек, который уже переведён на адаптивную подачу и должен на ней остаться. */
  private adaptiveTrack: string | null = null;

  /** Смена выбранного качества обнуляет всю накопленную историю откатов. */
  reset(): void {
    this.fellBack.clear();
    this.adaptiveTrack = null;
    this.degradations = 0;
  }

  /**
   * Отмечает обрыв. Возвращает `true`, если это первый обрыв с прошлого восстановления, —
   * вызывающий по нему решает, показывать ли сообщение. Намерение слушать «липкое»:
   * повторная ошибка прилетает уже на поставленном на паузу плеере.
   */
  fail(trackId: string | undefined, resume: boolean): boolean {
    const first = this.failed === null;

    if (trackId) {
      this.failed = { trackId, resume: resume || this.failed?.resume === true };
    }

    return first;
  }

  /**
   * Готовит пересборку источника после обрыва. `null` — восстанавливать нечего;
   * иначе `resume` говорит, слушал ли человек в момент обрыва.
   */
  recover(): { resume: boolean } | null {
    const failed = this.failed;
    if (!failed) return null;

    this.failed = null;
    this.retry = { ...this.retry, attempts: 0 };

    return { resume: failed.resume };
  }

  /** Источник пересобран штатно — прошлый обрыв больше не считается. */
  clearFailure(): void {
    this.failed = null;
  }

  /** Уводит подачу на ступень ниже и назначает выдержку, растущую с каждым разом. */
  degrade(now = Date.now()): void {
    this.degradedUntil = now + adaptiveCooldownMs(this.degradations);
    this.degradations += 1;
  }

  coolingDown(now = Date.now()): boolean {
    return this.degradedUntil > now;
  }

  /**
   * Нужно ли подавать этот трек адаптивно вместо прямого потока. Заодно фиксирует выбор:
   * трек, once переведённый на адаптивную подачу, на ней и остаётся — иначе он бы прыгал
   * туда-сюда на каждой перерисовке.
   */
  forceAdaptive(quality: AudioQuality, networkIsSlow: boolean, trackId: string): boolean {
    if (quality !== "Original") return false;

    if (networkIsSlow && !this.coolingDown()) this.degrade();

    const forced = networkIsSlow || this.coolingDown() || this.adaptiveTrack === trackId;
    if (forced) this.adaptiveTrack = trackId;

    return forced;
  }

  /** Ступень, на которой стоит подавать трек с учётом уже случившихся откатов. */
  tierFor(
    track: Track,
    quality: AudioQuality,
    qualities: { quality: AudioQuality }[],
    fallbackTier: AudioQuality | null,
  ): AudioQuality {
    if (quality === "Original" && this.fellBack.has(track.id)) {
      return fallbackTier ?? "Original";
    }

    return playableTier(track.codec, quality, qualities);
  }

  /** Источник загрузился: с этой ступени и считаем попытки. */
  loaded(trackId: string, tier: AudioQuality): void {
    this.retry = { trackId, tier, attempts: 0 };
  }

  /** Звук пошёл — счётчик попыток больше не нужен. */
  playing(): void {
    this.retry.attempts = 0;
  }

  /**
   * Что делать с ошибкой элемента. Возвращает решение и **уже применяет** его к своему
   * состоянию: отмечает откат, наращивает попытки. Вызывающему остаётся только побочная
   * часть — сообщение, новый src, таймер.
   */
  decide(input: {
    trackId: string;
    errorCode: number | undefined;
    offline: boolean;
    fallbackTier: AudioQuality | null;
    tier: AudioQuality;
  }): Recovery {
    if (this.retry.trackId !== input.trackId) {
      this.retry = { trackId: input.trackId, tier: input.tier, attempts: 0 };
    }

    const recovery = decideRecovery({
      errorCode: input.errorCode,
      tier: this.retry.tier,
      fallbackTier: input.fallbackTier,
      fellBack: this.fellBack.has(input.trackId),
      attempts: this.retry.attempts,
      sessionRenewed: this.retry.attempts > 0,
      offline: input.offline,
    });

    if (recovery.kind === "fallback") {
      this.fellBack.add(input.trackId);
      this.retry = { trackId: input.trackId, tier: recovery.tier, attempts: 0 };
      this.degrade();
    }

    if (recovery.kind === "retry") this.retry.attempts = recovery.attempt + 1;

    return recovery;
  }
}
