// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMemo, useSyncExternalStore } from "react";
import { useSettings } from "@/contexts/SettingsContext";
import { recapWindow, type RecapWindow } from "@/lib/recapWindow";

const subscribe = () => () => {};

/**
 * Окно итогов месяца или `null`, пока страница не ожила в браузере.
 *
 * Серверный снимок намеренно пустой: местная дата слушателя на сервере неизвестна, а угадав
 * её по часам сервера, мы получили бы расхождение при гидратации — то есть мигающий баннер
 * и прыгающий пункт меню на границе суток.
 */
export function useRecapWindow(): RecapWindow | null {
  const settings = useSettings();
  const mounted = useSyncExternalStore(
    subscribe,
    () => true,
    () => false,
  );

  return useMemo(
    () => (mounted ? recapWindow(new Date(), settings.timeZone) : null),
    [mounted, settings.timeZone],
  );
}
