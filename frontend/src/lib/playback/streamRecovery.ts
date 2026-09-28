// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export const STREAM_RETRY_DELAYS_MS = [800, 2500, 6000, 15_000, 30_000];

export const TRANSCODE_WAIT_DELAYS_MS = [1500, 4000, 9000, 18000];

export const ADAPTIVE_COOLDOWN_STEPS_MS = [5 * 60_000, 15 * 60_000, 45 * 60_000, 120 * 60_000];

export function adaptiveCooldownMs(previousDegradations: number): number {
  const step = Math.max(0, Math.min(previousDegradations, ADAPTIVE_COOLDOWN_STEPS_MS.length - 1));
  return ADAPTIVE_COOLDOWN_STEPS_MS[step];
}

const MEDIA_ERR_DECODE = 3;

const MEDIA_ERR_SRC_NOT_SUPPORTED = 4;

export type Recovery =
  | { kind: "fallback" }
  | { kind: "unsupported" }
  | { kind: "offline" }
  | { kind: "retry"; attempt: number; delayMs: number }
  | { kind: "giveUp" };

export function decideRecovery({
  errorCode,
  canAdapt,
  fellBack,
  attempts,
  sessionRenewed = true,
  offline = false,
}: {
  errorCode?: number;
  canAdapt: boolean;
  fellBack: boolean;
  attempts: number;
  sessionRenewed?: boolean;
  offline?: boolean;
}): Recovery {
  // INFO: без сети незачем жечь попытки — ждём возвращения связи и пересобираем источник тогда.
  if (offline) return { kind: "offline" };

  const undecodable = errorCode === MEDIA_ERR_DECODE || errorCode === MEDIA_ERR_SRC_NOT_SUPPORTED;

  // Прямой поток — всегда оригинал. Не декодируется он — дальше одна дорога: адаптивный поток,
  // который сервер перекодирует в AAC. Нет и его — формат этому браузеру не по силам.
  if (undecodable && !fellBack && sessionRenewed) {
    return canAdapt ? { kind: "fallback" } : { kind: "unsupported" };
  }

  const delays = fellBack ? TRANSCODE_WAIT_DELAYS_MS : STREAM_RETRY_DELAYS_MS;
  if (attempts >= delays.length) return { kind: "giveUp" };

  return { kind: "retry", attempt: attempts, delayMs: delays[attempts] };
}
