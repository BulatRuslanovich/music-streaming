// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { HydrationBoundary } from "@tanstack/react-query";
import { FavoritesPage, FAVORITES_PAGE_SIZE } from "@/app/favorites/FavoritesPage";
import { queries } from "@/lib/queries";
import { prefetchOnServer } from "@/lib/server/prefetch";

export default async function Page() {
  const state = await prefetchOnServer((client) =>
    client.prefetchQuery(queries.favorites({ page: 1, pageSize: FAVORITES_PAGE_SIZE })),
  );

  return (
    <HydrationBoundary state={state}>
      <FavoritesPage />
    </HydrationBoundary>
  );
}
