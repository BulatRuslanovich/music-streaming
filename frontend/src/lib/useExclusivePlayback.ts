"use client";

import { useEffect, useRef } from "react";
import { deviceId } from "@/lib/events";
import { API_BASE, refreshSession } from "@/lib/http";

/**
 * Паузы между попытками переоткрыть оборвавшийся поток. Кончились — сдаёмся молча.
 *
 * Отказ здесь не должен ничего останавливать: без потока теряется лишь взаимное вытеснение, а
 * музыка играет. Замолчать из-за неполадки в сети было бы куда хуже той беды, которую это лечит.
 */
const RECONNECT_DELAYS_MS = [1000, 3000, 8000];

/**
 * Держит за этим устройством единоличное право играть.
 *
 * <p>
 * Поток открыт ровно пока идёт воспроизведение, и само открытие — это заявка: сервер отдаёт право
 * последнему подключившемуся, а прежнему шлёт `displaced`. Молчащему устройству поток не нужен,
 * поэтому пауза его закрывает, а право освобождает.
 * </p>
 *
 * @param isPlaying Играет ли это устройство прямо сейчас.
 * @param onDisplaced Зовётся, когда право забрали: здесь плеер встаёт на паузу.
 */
export function useExclusivePlayback(isPlaying: boolean, onDisplaced: () => void): void {
  // Обработчик замыкает на себя перевод в паузу и уведомление, и меняется он на каждой отрисовке.
  // Через ссылку эффект не пересоздаётся из-за него — иначе поток переоткрывался бы без нужды,
  // а каждое открытие это новая заявка.
  const displaced = useRef(onDisplaced);
  useEffect(() => {
    displaced.current = onDisplaced;
  });

  useEffect(() => {
    if (!isPlaying) return;

    let source: EventSource | null = null;
    let timer: number | null = null;
    let attempt = 0;
    let stopped = false;

    const open = () => {
      if (stopped) return;

      source = new EventSource(
        `${API_BASE}/playback/session?deviceId=${encodeURIComponent(deviceId())}`,
      );

      source.addEventListener("open", () => {
        attempt = 0;
      });

      source.addEventListener("displaced", () => {
        stopped = true;
        source?.close();
        displaced.current();
      });

      source.addEventListener("error", () => {
        // Оборванный поток браузер переоткрывает сам и остаётся в CONNECTING; в CLOSED он уходит,
        // упёршись в ответ с ошибкой, — и вот тогда переоткрывать приходится вручную.
        if (source?.readyState !== EventSource.CLOSED) return;

        source.close();
        source = null;

        const delay = RECONNECT_DELAYS_MS[attempt];
        if (delay === undefined) return;
        attempt += 1;

        timer = window.setTimeout(() => {
          timer = null;

          // Самая вероятная причина закрытия — истёкший токен доступа: он живёт тридцать минут, а
          // пластинка может играть дольше. Продление идёт общее на всё приложение и само себя
          // сводит, если параллельно за него взялся кто-то ещё.
          void refreshSession().finally(open);
        }, delay);
      });
    };

    open();

    return () => {
      stopped = true;
      if (timer !== null) window.clearTimeout(timer);
      source?.close();
    };
  }, [isPlaying]);
}
