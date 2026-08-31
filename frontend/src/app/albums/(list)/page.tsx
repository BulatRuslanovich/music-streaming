// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { HydrationBoundary } from "@tanstack/react-query";
import { AlbumsPage, ALBUMS_PAGE_SIZE } from "@/app/albums/(list)/AlbumsPage";
import { queries } from "@/lib/queries";
import { prefetchOnServer } from "@/lib/server/prefetch";

export default async function Page() {
  const state = await prefetchOnServer((client) =>
    Promise.all([
      client.prefetchInfiniteQuery(queries.albumsFeed({ pageSize: ALBUMS_PAGE_SIZE })),
      client.prefetchQuery(queries.libraryOverview()),
    ]),
  );

  return (
    <HydrationBoundary state={state}>
      <AlbumsPage />
    </HydrationBoundary>
  );
}
