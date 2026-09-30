// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { HydrationBoundary } from "@tanstack/react-query";
import { ArtistPage } from "@/app/artists/[id]/ArtistPage";
import { queries } from "@/lib/queries";
import { prefetchOnServer } from "@/lib/server/prefetch";

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const state = await prefetchOnServer((client) =>
    Promise.all([
      client.prefetchQuery(queries.artist(id)),
      client.prefetchQuery(queries.artistTopTracks(id)),
    ]),
  );

  return (
    <HydrationBoundary state={state}>
      <ArtistPage />
    </HydrationBoundary>
  );
}
