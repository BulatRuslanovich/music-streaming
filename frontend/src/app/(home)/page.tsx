// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { HydrationBoundary } from "@tanstack/react-query";
import { HomePage } from "@/app/(home)/HomePage";
import { queries } from "@/lib/queries";
import { prefetchOnServer } from "@/lib/server/prefetch";

export default async function Page() {
  const state = await prefetchOnServer((client) => client.prefetchQuery(queries.homeFeed()));

  return (
    <HydrationBoundary state={state}>
      <HomePage />
    </HydrationBoundary>
  );
}
