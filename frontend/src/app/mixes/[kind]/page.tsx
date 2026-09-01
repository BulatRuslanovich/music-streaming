// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { HydrationBoundary } from "@tanstack/react-query";
import { MixPage, isMixSlug } from "@/app/mixes/[kind]/MixPage";
import { queries } from "@/lib/queries";
import { prefetchOnServer } from "@/lib/server/prefetch";

export default async function Page({ params }: { params: Promise<{ kind: string }> }) {
  const { kind } = await params;

  // Проверку вида микса переиспользуем из компонента, а не заводим второй список: чужой слаг
  // здесь просто не греем, а 404 по-прежнему выдаёт сам компонент.
  const state = isMixSlug(kind)
    ? await prefetchOnServer((client) => client.prefetchQuery(queries.homeMix(kind)))
    : undefined;

  return (
    <HydrationBoundary state={state}>
      <MixPage />
    </HydrationBoundary>
  );
}
