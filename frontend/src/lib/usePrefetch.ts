// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQueryClient, type FetchQueryOptions } from "@tanstack/react-query";

/** Прогрев запроса по наведению на карточку: к клику ответ обычно уже в кэше. */
export function usePrefetch<TData, TKey extends readonly unknown[]>(
  options: FetchQueryOptions<TData, Error, TData, TKey>,
) {
  const client = useQueryClient();
  return () => void client.prefetchQuery(options);
}
