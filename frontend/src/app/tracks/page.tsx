// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { HydrationBoundary } from "@tanstack/react-query";
import { TracksPage } from "@/app/tracks/TracksPage";
import { TRACK_PAGE_SIZE } from "@/lib/pageSizes";
import { queries } from "@/lib/queries";
import { prefetchOnServer } from "@/lib/server/prefetch";

export default async function Page() {
  // Страница всегда открывается с первой страницы и сортировкой по умолчанию — состояние
  // пагинации живёт в компоненте, а не в URL, поэтому ключ здесь детерминирован.
  const state = await prefetchOnServer((client) =>
    Promise.all([
      client.prefetchQuery(queries.tracks({ page: 1, pageSize: TRACK_PAGE_SIZE, sort: "Title" })),
      client.prefetchQuery(queries.libraryOverview()),
    ]),
  );

  return (
    <HydrationBoundary state={state}>
      <TracksPage />
    </HydrationBoundary>
  );
}
