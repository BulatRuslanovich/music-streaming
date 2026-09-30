// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { AudioQuality } from "@/lib/types";

export const STREAM_RETRY_DELAYS_MS = [800, 2500, 6000, 15_000, 30_000];

/** Между попытками, пока сервер перекодирует трек, не проигравшийся в оригинале. */
export const TRANSCODE_WAIT_DELAYS_MS = [1500, 4000, 9000, 18000];

const MEDIA_ERR_DECODE = 3;
const MEDIA_ERR_SRC_NOT_SUPPORTED = 4;

export type Recovery =
  | { kind: "fallback" }
  | { kind: "offline" }
  | { kind: "retry"; attempt: number; delayMs: number }
  | { kind: "giveUp" };

/**
 * Состояние восстановления воспроизведения: попытки, откаты с оригинала на адаптивный поток и
 * память об оборванном источнике.
 *
 * Обычный объект с явными переходами, как `AdaptivePlayback` рядом, а не россыпь `useRef`
 * внутри `usePlaybackEngine`: там эти поля попали бы в зависимости центрального эффекта, и
 * изменение любого пересобирало бы источник — то есть обрывало звук.
 */
export class PlaybackRecovery {
  private retry: { trackId: string; attempts: number } = { trackId: "", attempts: 0 };

  /** Оборванный источник: `<audio>` остался с мёртвым src и сам не оживёт. */
  private failed: { trackId: string; resume: boolean } | null = null;

  /** Треки, чей оригинал браузер не смог декодировать: они играют адаптивно. */
  private readonly fellBack = new Set<string>();

  /** Оригинал уже захлёбывался: сеть его не тянет, и дальше он подаётся адаптивно. */
  private stalled = false;

  /** Смена качества — явный выбор слушателя, и накопленные откаты ему не мешают. */
  reset(): void {
    this.fellBack.clear();
    this.stalled = false;
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

  /**
   * Оригинал захлебнулся: до конца сессии или до смены качества он подаётся адаптивно.
   * Без выдержек и возвратов — сеть, которая не тянула оригинал минуту назад, вряд ли
   * вытянет его сейчас, а прыжки туда-сюда слышны сильнее, чем пониженный битрейт.
   */
  degrade(): void {
    this.stalled = true;
  }

  get degraded(): boolean {
    return this.stalled;
  }

  forceAdaptive(quality: AudioQuality, trackId: string): boolean {
    return quality === "Original" && (this.stalled || this.fellBack.has(trackId));
  }

  /** Источник загрузился: с него и считаем попытки. */
  loaded(trackId: string): void {
    this.retry = { trackId, attempts: 0 };
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
  decide(input: { trackId: string; errorCode: number | undefined; offline: boolean }): Recovery {
    if (this.retry.trackId !== input.trackId) {
      this.retry = { trackId: input.trackId, attempts: 0 };
    }

    // Без сети незачем жечь попытки — ждём связь и пересобираем источник тогда.
    if (input.offline) return { kind: "offline" };

    const fellBack = this.fellBack.has(input.trackId);
    const undecodable =
      input.errorCode === MEDIA_ERR_DECODE || input.errorCode === MEDIA_ERR_SRC_NOT_SUPPORTED;

    // Прямой поток — всегда оригинал. Не декодируется он — дальше одна дорога: адаптивный
    // поток, который сервер перекодирует в AAC. Но не с первой ошибки: первая может быть
    // протухшей сессией, и её лечит повтор с обновлённым токеном.
    if (undecodable && !fellBack && this.retry.attempts > 0) {
      this.fellBack.add(input.trackId);
      this.retry = { trackId: input.trackId, attempts: 0 };
      return { kind: "fallback" };
    }

    const delays = fellBack ? TRANSCODE_WAIT_DELAYS_MS : STREAM_RETRY_DELAYS_MS;
    const attempt = this.retry.attempts;
    if (attempt >= delays.length) return { kind: "giveUp" };

    this.retry.attempts = attempt + 1;
    return { kind: "retry", attempt, delayMs: delays[attempt] };
  }
}
