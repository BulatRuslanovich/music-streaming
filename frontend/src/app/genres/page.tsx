// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { HydrationBoundary } from "@tanstack/react-query";
import { GenresPage } from "@/app/genres/GenresPage";
import { queries } from "@/lib/queries";
import { prefetchOnServer } from "@/lib/server/prefetch";

export default async function Page() {
  // Треки жанра зависят от выбора, который делается уже на клиенте, — греем только список.
  const state = await prefetchOnServer((client) => client.prefetchQuery(queries.genres()));

  return (
    <HydrationBoundary state={state}>
      <GenresPage />
    </HydrationBoundary>
  );
}
