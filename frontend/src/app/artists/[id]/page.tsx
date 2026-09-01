// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { HydrationBoundary } from "@tanstack/react-query";
import { ArtistPage } from "@/app/artists/[id]/ArtistPage";
import { TRACK_PAGE_SIZE } from "@/lib/pageSizes";
import { queries } from "@/lib/queries";
import { prefetchOnServer } from "@/lib/server/prefetch";

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const state = await prefetchOnServer((client) =>
    Promise.all([
      client.prefetchQuery(queries.artist(id, { page: 1, pageSize: TRACK_PAGE_SIZE })),
      client.prefetchQuery(queries.artistTopTracks(id)),
    ]),
  );

  return (
    <HydrationBoundary state={state}>
      <ArtistPage />
    </HydrationBoundary>
  );
}
