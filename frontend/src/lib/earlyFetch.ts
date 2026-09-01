// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export const PRELOAD_GLOBAL = "__msPreload";

export const SESSION_HINT_COOKIE = "ms_session";

/**
 * Что запросить из <head>, ещё до загрузки бандла.
 *
 * Здесь остался только профиль: его `AuthProvider` запрашивает на каждом монтировании, так что
 * фора реальная. Домашний фид отсюда убран — страница рендерится на сервере и приезжает вместе
 * с данными, поэтому клиент за ними уже не идёт, а предзагрузка качала бы самый большой JSON
 * входной страницы второй раз и выбрасывала бы результат.
 */
const PRELOAD = ["/api/auth/me"];

export const EARLY_FETCH_SCRIPT = `try {
  if (document.cookie.indexOf("${SESSION_HINT_COOKIE}=") !== -1) {
    var paths = ${JSON.stringify(PRELOAD)};
    var store = (window.${PRELOAD_GLOBAL} = {});
    for (var i = 0; i < paths.length; i++) {
      (function (path) {
        store[path] = fetch(path, { credentials: "include" }).catch(function () {
          return null;
        });
      })(paths[i]);
    }
  }
} catch (e) {}`;

type PreloadStore = Record<string, Promise<Response | null> | undefined>;

export async function takePreloaded(url: string): Promise<Response | null> {
  if (typeof window === "undefined") return null;

  const store = (window as unknown as Record<string, PreloadStore | undefined>)[PRELOAD_GLOBAL];
  const pending = store?.[url];
  if (!store || !pending) return null;

  delete store[url];

  const response = await pending;
  return response ?? null;
}
