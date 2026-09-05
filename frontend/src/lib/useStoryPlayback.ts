// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useCallback, useEffect, useRef, useState } from "react";

/** Сколько держится слайд. Хватает прочитать строку и рассмотреть арт, но не заскучать. */
export const SLIDE_MS = 6_000;

interface StoryPlayback {
  index: number;
  paused: boolean;
  next: () => void;
  previous: () => void;
  hold: (held: boolean) => void;
}

/**
 * Ход истории: сам продвигается, пока его не придержат.
 *
 * Прогресс здесь не считается — полоску рисует CSS-анимация той же длительности, и пауза у
 * них общая. Иначе ширину пришлось бы гнать через состояние, то есть перерисовывать экран с
 * обложками каждый кадр ради одного числа.
 */
export function useStoryPlayback({
  count,
  autoplay,
  onFinish,
}: {
  count: number;
  autoplay: boolean;
  onFinish: () => void;
}): StoryPlayback {
  const [index, setIndex] = useState(0);
  const [held, setHeld] = useState(false);
  const [hidden, setHidden] = useState(false);

  // Остаток текущего слайда: удержание замирает на месте, а не отматывает слайд к началу.
  const remaining = useRef(SLIDE_MS);

  // Обработчик держим в ref, а не в зависимостях: он приходит новой функцией на каждый
  // рендер и перезапускал бы отсчёт слайда.
  const finish = useRef(onFinish);
  useEffect(() => {
    finish.current = onFinish;
  });

  // Вкладку увели — история не должна проматываться в пустоту и кончиться к возвращению.
  useEffect(() => {
    const onVisibility = () => setHidden(document.visibilityState === "hidden");
    onVisibility();
    document.addEventListener("visibilitychange", onVisibility);
    return () => document.removeEventListener("visibilitychange", onVisibility);
  }, []);

  const paused = held || hidden;

  // Индекс читается из замыкания, а не из updater'а: закрытие истории — побочный эффект,
  // а внутри updater'а React волен вызвать его дважды и ругается на обновление чужого
  // компонента во время рендера.
  const next = useCallback(() => {
    remaining.current = SLIDE_MS;
    if (index + 1 >= count) {
      finish.current();
      return;
    }
    setIndex(index + 1);
  }, [index, count]);

  const previous = useCallback(() => {
    remaining.current = SLIDE_MS;
    setIndex(Math.max(0, index - 1));
  }, [index]);

  const deadline = useRef(0);

  useEffect(() => {
    if (!autoplay) return;

    // Остаток снимается ровно на паузе. Считать его в cleanup нельзя: тот же cleanup срабатывает
    // и на смене слайда, и тогда из свежих шести секунд вычиталось бы всё прошлое ожидание.
    if (paused) {
      remaining.current = Math.max(0, deadline.current - performance.now());
      return;
    }

    deadline.current = performance.now() + remaining.current;
    const timer = window.setTimeout(next, remaining.current);

    return () => window.clearTimeout(timer);
  }, [autoplay, paused, index, next]);

  return { index, paused, next, previous, hold: setHeld };
}
