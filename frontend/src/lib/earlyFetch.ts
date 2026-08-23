// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export const PRELOAD_GLOBAL = "__msPreload";

export const SESSION_HINT_COOKIE = "ms_session";

/**
 * Пути, которые имеет смысл запросить до гидрации, — в привязке к маршруту, с которого
 * началась загрузка. Ключ должен совпадать с тем, что соберёт `request()` из lib/http,
 * иначе ответ просто не будет подобран и уйдёт в мусор.
 */
const PRELOAD_BY_ROUTE: Record<string, string[]> = {
  "/": ["/api/auth/me", "/api/home/feed?sectionSize=12"],
};

const FALLBACK_PRELOAD = ["/api/auth/me"];

/**
 * Раньше первый байт данных запрашивался только после того, как браузер скачал и разобрал
 * весь клиентский бандл: `me()` летел из useEffect после гидратации, а запрос страницы —
 * ещё одним RTT позже. Этот скрипт стартует те же запросы сразу по приходу HTML, так что
 * сеть работает параллельно с парсингом JS, а `request()` потом подбирает готовый ответ.
 *
 * Запускается только при живой сессии: анониму всё равно ехать на /login.
 */
export const EARLY_FETCH_SCRIPT = `try {
  if (document.cookie.indexOf("${SESSION_HINT_COOKIE}=") !== -1) {
    var byRoute = ${JSON.stringify(PRELOAD_BY_ROUTE)};
    var paths = byRoute[location.pathname] || ${JSON.stringify(FALLBACK_PRELOAD)};
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

/**
 * Ответ одноразовый — тело Response читается один раз, поэтому запись сразу вынимается
 * из хранилища. Повторный запрос того же пути (рефетч, инвалидация) пойдёт обычным путём.
 */
export async function takePreloaded(url: string): Promise<Response | null> {
  if (typeof window === "undefined") return null;

  const store = (window as unknown as Record<string, PreloadStore | undefined>)[PRELOAD_GLOBAL];
  const pending = store?.[url];
  if (!store || !pending) return null;

  delete store[url];

  const response = await pending;
  return response ?? null;
}
