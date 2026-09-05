// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { HydrationBoundary } from "@tanstack/react-query";
import { TagsPage } from "@/app/tags/TagsPage";
import { queries } from "@/lib/queries";
import { prefetchOnServer } from "@/lib/server/prefetch";

export default async function Page() {
  // Содержимое тега зависит от выбора, который приезжает в адресе, — греем только список.
  const state = await prefetchOnServer((client) => client.prefetchQuery(queries.tags()));

  return (
    <HydrationBoundary state={state}>
      <TagsPage />
    </HydrationBoundary>
  );
}
