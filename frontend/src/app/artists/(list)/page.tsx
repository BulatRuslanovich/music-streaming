// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { HydrationBoundary } from "@tanstack/react-query";
import { ArtistsPage, ARTISTS_PAGE_SIZE } from "@/app/artists/(list)/ArtistsPage";
import { queries } from "@/lib/queries";
import { prefetchOnServer } from "@/lib/server/prefetch";

export default async function Page() {
  const state = await prefetchOnServer((client) =>
    Promise.all([
      client.prefetchInfiniteQuery(queries.artistsFeed({ pageSize: ARTISTS_PAGE_SIZE })),
      client.prefetchQuery(queries.libraryOverview()),
    ]),
  );

  return (
    <HydrationBoundary state={state}>
      <ArtistsPage />
    </HydrationBoundary>
  );
}
