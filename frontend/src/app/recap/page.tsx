// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { HydrationBoundary } from "@tanstack/react-query";
import { Suspense } from "react";
import { RecapPage } from "@/app/recap/RecapPage";
import { queries } from "@/lib/queries";
import { prefetchOnServer } from "@/lib/server/prefetch";

export default async function Page() {
  const state = await prefetchOnServer((client) => client.prefetchQuery(queries.recapMonths()));

  return (
    <HydrationBoundary state={state}>
      <Suspense>
        <RecapPage />
      </Suspense>
    </HydrationBoundary>
  );
}
