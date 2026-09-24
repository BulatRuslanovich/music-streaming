// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import "server-only";
import { QueryClient, dehydrate, type DehydratedState } from "@tanstack/react-query";
import { cookies } from "next/headers";
import { ApiError } from "@/lib/http";
import { backendOrigin, requestContext } from "@/lib/server/requestContext";

/**
 * Готовит данные страницы на сервере и отдаёт снимок для HydrationBoundary.
 *
 * Смысл в том, чтобы HTML приезжал уже с содержимым: до этого каждый роут отдавал спиннер, и
 * первый реальный контент стоил цепочки HTML → бандл → hydrate → /auth/me → запрос страницы.
 *
 * Запросы идут теми же `queryOptions` из `queries.ts`, что и на клиенте, — ключи и функции
 * загрузки не дублируются. Неудача глотается намеренно: непрогретая страница просто догрузится
 * на клиенте, вместо пятисотки на весь роут.
 *
 * Но глотать молча — нельзя. Когда `BACKEND_INTERNAL_URL` не был задан в compose, серверные
 * запросы уходили внутрь собственного контейнера и падали на connection refused: префетч не
 * работал ни на одной странице, и ровно ничего об этом не сообщало. 401 сюда не относится —
 * это обычная истёкшая сессия, её разберёт proxy.
 */
export async function prefetchOnServer(
  prefetch: (client: QueryClient) => Promise<unknown>,
): Promise<DehydratedState> {
  const client = new QueryClient();
  const cookie = (await cookies()).toString();

  try {
    await requestContext.run({ cookie, origin: backendOrigin() }, () => prefetch(client));
  } catch (reason) {
    if (!(reason instanceof ApiError && reason.status === 401)) {
      console.warn(
        `[prefetch] server-side prefetch against ${backendOrigin()} failed:`,
        reason instanceof Error ? reason.message : reason,
      );
    }
  }

  return dehydrate(client);
}
