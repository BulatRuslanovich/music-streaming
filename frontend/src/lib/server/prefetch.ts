// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import "server-only";
import { QueryClient, dehydrate, type DehydratedState } from "@tanstack/react-query";
import { cookies } from "next/headers";
import { backendOrigin, requestContext } from "@/lib/server/requestContext";

/**
 * Готовит данные страницы на сервере и отдаёт снимок для HydrationBoundary.
 *
 * Смысл в том, чтобы HTML приезжал уже с содержимым: до этого каждый роут отдавал спиннер, и
 * первый реальный контент стоил цепочки HTML → бандл → hydrate → /auth/me → запрос страницы.
 *
 * Запросы идут теми же `queryOptions` из `queries.ts`, что и на клиенте, — ключи и функции
 * загрузки не дублируются. Неудача глотается намеренно: непрогретая страница просто догрузится
 * на клиенте, как раньше, вместо пятисотки на весь роут.
 */
export async function prefetchOnServer(
  prefetch: (client: QueryClient) => Promise<unknown>,
): Promise<DehydratedState> {
  const client = new QueryClient();
  const cookie = (await cookies()).toString();

  try {
    await requestContext.run({ cookie, origin: backendOrigin() }, () => prefetch(client));
  } catch {}

  return dehydrate(client);
}
