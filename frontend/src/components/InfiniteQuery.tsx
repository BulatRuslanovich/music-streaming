// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { UseInfiniteQueryResult } from "@tanstack/react-query";
import { useEffect, useRef, type ReactNode } from "react";
import type { Paged } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { EmptyState } from "./EmptyState";
import { LoadError } from "./Query";
import { SkeletonGroup, type SkeletonVariant } from "./ui/skeleton";

/**
 * То же, что `Query`, но для лент, которые дочитываются вниз. Состояния и их порядок
 * повторяют `Query` намеренно: скелет, ошибка с повтором, пустой экран — чтобы каталог
 * не выглядел иначе просто оттого, что листается по-другому.
 */
export function InfiniteQuery<T>({
  result,
  skeleton = "card",
  skeletonCount = 12,
  empty,
  children,
}: {
  result: UseInfiniteQueryResult<{ pages: Paged<T>[] }>;
  skeleton?: SkeletonVariant;
  skeletonCount?: number;
  empty?: { icon?: ReactNode; title: string; description?: string };
  children: (items: T[], total: number) => ReactNode;
}) {
  const t = useT();
  const { data, error, isPending, refetch, hasNextPage, isFetchingNextPage, fetchNextPage } =
    result;

  if (error && data === undefined) {
    return (
      <LoadError
        message={error instanceof Error ? error.message : t("error.load")}
        onRetry={() => void refetch()}
      />
    );
  }

  if (isPending) return <SkeletonGroup variant={skeleton} count={skeletonCount} />;
  if (data === undefined) return null;

  const items = data.pages.flatMap((page) => page.items);
  const total = data.pages[0]?.total ?? items.length;

  if (empty && total === 0) {
    return <EmptyState icon={empty.icon} title={empty.title} description={empty.description} />;
  }

  return (
    <>
      {children(items, total)}

      {hasNextPage && <LoadMore busy={isFetchingNextPage} onReach={() => void fetchNextPage()} />}
    </>
  );
}

/**
 * Дочитывает следующую страницу, когда до конца ленты остаётся экран с небольшим запасом.
 * Кнопка оставлена под сентинелом не «на всякий случай»: без указателя и без клавиатуры
 * (или когда IntersectionObserver недоступен) дочитать список было бы нечем.
 */
function LoadMore({ busy, onReach }: { busy: boolean; onReach: () => void }) {
  const t = useT();
  const sentinel = useRef<HTMLDivElement>(null);

  // Колбэк приходит новой функцией на каждый рендер, а рендер случается и на самой
  // подгрузке. Через ref наблюдатель ставится один раз и не пересобирается на каждый
  // кадр загрузки; `busy` там же, чтобы не звать подгрузку поверх уже идущей.
  const latest = useRef({ busy, onReach });

  useEffect(() => {
    latest.current = { busy, onReach };
  });

  useEffect(() => {
    const element = sentinel.current;
    if (!element) return;

    const observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting && !latest.current.busy) latest.current.onReach();
      },
      { rootMargin: "600px" },
    );

    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  return (
    <div ref={sentinel} className="flex justify-center pt-2 pb-6">
      <button
        type="button"
        onClick={onReach}
        disabled={busy}
        className="rounded-full px-4 py-2 text-sm font-semibold text-muted-foreground transition-colors duration-150 ease-brand hover:text-foreground disabled:opacity-60"
      >
        {busy ? t("common.loading") : t("pagination.loadMore")}
      </button>
    </div>
  );
}
