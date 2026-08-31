// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { PageHeader } from "@/components/PageHeader";
import { Skeleton, SkeletonGroup, type SkeletonVariant } from "@/components/ui/skeleton";
import { useT } from "@/contexts/I18nContext";
import type { TranslationKey } from "@/lib/i18n";

/**
 * Состояние загрузки роута.
 *
 * Повторяет обвязку страницы, а не только её содержимое. Голая сетка скелетов расходилась с тем,
 * что появлялось следом: заголовок и панель фильтров въезжали сверху и сдвигали всё вниз. Заголовок
 * здесь настоящий — он статический и известен без данных, так что и подменять его нечем.
 */
export function PageLoading({
  title,
  toolbar = false,
  variant = "card",
  count = 12,
}: {
  title: TranslationKey;
  /** Есть ли у страницы строка поиска и сортировки. */
  toolbar?: boolean;
  variant?: SkeletonVariant;
  count?: number;
}) {
  const t = useT();

  return (
    <>
      <PageHeader title={t(title)} />

      {toolbar && (
        <div className="flex flex-wrap items-center gap-3">
          {/* Высота повторяет SearchField: py-2.5 вокруг строки в 24px плюс рамка. */}
          <Skeleton className="h-[2.875rem] w-full max-w-xl" />
        </div>
      )}

      <SkeletonGroup variant={variant} count={count} />
    </>
  );
}
