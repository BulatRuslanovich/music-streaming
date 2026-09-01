// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { HydrationBoundary } from "@tanstack/react-query";
import { AlbumPage } from "@/app/albums/[id]/AlbumPage";
import { queries } from "@/lib/queries";
import { prefetchOnServer } from "@/lib/server/prefetch";

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  // Соседние альбомы артиста греть нечем: artistId известен только из самого альбома,
  // и лишний последовательный запрос на сервере отложил бы отдачу HTML.
  const state = await prefetchOnServer((client) => client.prefetchQuery(queries.album(id)));

  return (
    <HydrationBoundary state={state}>
      <AlbumPage />
    </HydrationBoundary>
  );
}
