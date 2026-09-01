// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { NextResponse, type NextRequest } from "next/server";

const ACCESS_COOKIE = "ms_access";
const REFRESH_COOKIE = "ms_refresh";
const SESSION_HINT_COOKIE = "ms_session";

const LOGIN_PATH = "/login";

// Обновляем чуть заранее: токен, которому осталось несколько секунд, к моменту серверного
// рендера страницы уже протухнет.
const RENEW_WINDOW_MS = 30_000;

function backendUrl(): string {
  return process.env.BACKEND_INTERNAL_URL ?? "http://localhost:5199";
}

/**
 * Срок действия access-токена без проверки подписи.
 *
 * Проверять подпись здесь не нужно и нечем: решение принимает бэкенд, а middleware лишь выбирает,
 * стоит ли сходить за новой парой кук. Ошибка в любую сторону стоит одного лишнего запроса.
 */
function expiresAt(token: string): number | null {
  const payload = token.split(".")[1];
  if (!payload) return null;

  try {
    const json = atob(payload.replace(/-/g, "+").replace(/_/g, "/"));
    const exp = (JSON.parse(json) as { exp?: unknown }).exp;
    return typeof exp === "number" ? exp * 1000 : null;
  } catch {
    return null;
  }
}

function needsRenewal(request: NextRequest): boolean {
  if (!request.cookies.has(REFRESH_COOKIE)) return false;

  const access = request.cookies.get(ACCESS_COOKIE)?.value;
  if (!access) return true;

  const deadline = expiresAt(access);
  return deadline === null ? false : deadline - Date.now() < RENEW_WINDOW_MS;
}

/**
 * Обновляет сессию до того, как страница начнёт рендериться на сервере.
 *
 * Серверный компонент не может выставить куку, а access-токен живёт десять минут — без этого шага
 * серверный префетч ловил бы 401 на большинстве холодных заходов и страница откатывалась бы к
 * клиентской загрузке, то есть ровно к тому водопаду, который мы убираем.
 */
async function renew(
  request: NextRequest,
): Promise<{ cookie: string; setCookie: string[] } | null> {
  try {
    const response = await fetch(`${backendUrl()}/api/auth/refresh`, {
      method: "POST",
      headers: { cookie: request.headers.get("cookie") ?? "" },
      cache: "no-store",
    });

    if (!response.ok) return null;

    const setCookie = response.headers.getSetCookie();
    if (setCookie.length === 0) return null;

    // Собираем заголовок cookie для рендера: свежие значения поверх пришедших от браузера.
    const merged = new Map<string, string>();
    for (const cookie of request.cookies.getAll()) merged.set(cookie.name, cookie.value);
    for (const raw of setCookie) {
      const [pair] = raw.split(";");
      const separator = pair.indexOf("=");
      if (separator > 0) merged.set(pair.slice(0, separator).trim(), pair.slice(separator + 1));
    }

    return {
      cookie: [...merged].map(([name, value]) => `${name}=${value}`).join("; "),
      setCookie,
    };
  } catch {
    return null;
  }
}

export async function middleware(request: NextRequest): Promise<NextResponse> {
  const { pathname } = request.nextUrl;
  const onLoginPage = pathname === LOGIN_PATH;

  const renewal = needsRenewal(request) ? await renew(request) : null;

  // Подсказка о сессии не HttpOnly и живёт столько же, сколько refresh: по ней и решаем, кто перед
  // нами, не дожидаясь /auth/me на клиенте.
  const signedIn = renewal !== null || request.cookies.has(SESSION_HINT_COOKIE);

  if (!signedIn && !onLoginPage) {
    const target = request.nextUrl.clone();
    target.pathname = LOGIN_PATH;
    target.search = "";
    return NextResponse.redirect(target);
  }

  if (signedIn && onLoginPage) {
    const target = request.nextUrl.clone();
    target.pathname = "/";
    target.search = "";
    return NextResponse.redirect(target);
  }

  if (!renewal) return NextResponse.next();

  const headers = new Headers(request.headers);
  headers.set("cookie", renewal.cookie);

  const response = NextResponse.next({ request: { headers } });
  for (const cookie of renewal.setCookie) response.headers.append("set-cookie", cookie);

  return response;
}

export const config = {
  // Только навигации и RSC-запросы страниц: статика, API и service worker сюда не заходят.
  //
  // Всё с расширением в последнем сегменте — это файл из public/, и он обязан остаться снаружи.
  // Оптимизатор картинок за /_next/image ходит за исходником внутренним запросом без кук: он
  // выглядит как аноним, ловит редирект на /login и отдаёт 400 вместо картинки — то есть логотип
  // пропадает и у залогиненных тоже. Поимённый список тут уже один раз протёк.
  matcher: ["/((?!api|_next/static|_next/image|icons|.*\\.[^/]+$).*)"],
};
