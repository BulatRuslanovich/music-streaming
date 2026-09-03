// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { useCallback } from "react";
import { useT } from "@/contexts/I18nContext";
import { ToggleGroup, ToggleGroupButton } from "@/components/ui/tabs";
import type { StatisticsPeriod } from "@/lib/types";

const PERIODS: StatisticsPeriod[] = ["Week", "Month", "Quarter", "Year", "All"];

export function PeriodTabs({
  period,
  onChange,
}: {
  period: StatisticsPeriod;
  onChange: (period: StatisticsPeriod) => void;
}) {
  const t = useT();

  return (
    <ToggleGroup aria-label={t("stats.periodLabel")}>
      {PERIODS.map((value) => (
        <ToggleGroupButton key={value} active={value === period} onClick={() => onChange(value)}>
          {t(`stats.period.${value}` as const)}
        </ToggleGroupButton>
      ))}
    </ToggleGroup>
  );
}

/**
 * Фильтры раздела живут в адресе.
 *
 * Иначе на выборку нельзя дать ссылку, а «назад» в браузере уносит со страницы вместо возврата
 * к прошлому фильтру. Так же сделано на `/statistics`, `/search` и `/settings` — общего хука в
 * проекте не было, потому что до сих пор это требовалось одной странице за раз; здесь их три.
 */
export function useUrlFilters(basePath: string) {
  const router = useRouter();
  const params = useSearchParams();

  const set = useCallback(
    (patch: Record<string, string | undefined>) => {
      const next = new URLSearchParams(params.toString());

      for (const [key, value] of Object.entries(patch)) {
        // Пустое значение — это «фильтра нет», и в адресе ему делать нечего: пустые параметры
        // накапливаются и превращают ссылку в мусор.
        if (value === undefined || value === "") next.delete(key);
        else next.set(key, value);
      }

      const search = next.toString();
      router.replace(search === "" ? basePath : `${basePath}?${search}`);
    },
    [basePath, params, router],
  );

  return { params, set };
}
