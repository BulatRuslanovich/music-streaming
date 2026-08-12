"use client";

import { useCallback, useState } from "react";
import { useApi } from "@/lib/useApi";
import type { Paged } from "@/lib/types";

interface PagedQuery<T> {
  data: Paged<T> | null;
  error: string | null;
  loading: boolean;
  page: number;
  setPage: (page: number) => void;
  reload: () => void;
  patch: (updater: (current: Paged<T> | null) => Paged<T> | null) => void;
}

export function usePagedApi<T>(
  loader: (page: number) => Promise<Paged<T> | null>,
  deps: unknown[] = [],
  cacheKey?: string,
): PagedQuery<T> {
  const depsKey = JSON.stringify(deps);
  const [paging, setPaging] = useState({ depsKey, page: 1 });

  if (paging.depsKey !== depsKey) {
    setPaging({ depsKey, page: 1 });
  }

  const page = paging.depsKey === depsKey ? paging.page : 1;

  const setPage = useCallback(
    (next: number) => setPaging((current) => ({ ...current, page: next })),
    [],
  );

  const query = useApi(() => loader(page), [page, ...deps], cacheKey);

  return { ...query, page, setPage };
}
