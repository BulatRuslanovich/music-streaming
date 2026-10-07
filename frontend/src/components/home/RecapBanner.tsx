// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { CalendarRangeIcon, ChevronRightIcon } from "lucide-react";
import type { Route } from "next";
import Link from "next/link";
import { monthKey } from "@/lib/recap";
import { queries } from "@/lib/queries";
import { useFormat } from "@/lib/useFormat";
import { useMonthLabel } from "@/lib/useMonthLabel";
import { useT } from "@/contexts/I18nContext";

// Первую неделю месяца главная напоминает об итогах прошлого; дальше они живут в библиотеке.
const SHOWN_DAYS = 7;

export function RecapBanner() {
  const t = useT();
  const format = useFormat();
  const label = useMonthLabel();
  const months = useQuery(queries.recapMonths());

  const today = new Date();
  if (today.getDate() > SHOWN_DAYS) return null;

  const previous = new Date(today.getFullYear(), today.getMonth() - 1, 1);
  const recap = months.data?.find(
    (item) => item.year === previous.getFullYear() && item.month === previous.getMonth() + 1,
  );
  if (!recap) return null;

  return (
    <Link
      href={`/recap?month=${monthKey(recap.year, recap.month)}` as Route}
      className="group flex items-center gap-4 rounded-md bg-card p-4 transition-colors duration-150 ease-brand hover:bg-raised hover:no-underline"
    >
      <span className="grid size-12 shrink-0 place-items-center rounded-full bg-primary-soft text-primary">
        <CalendarRangeIcon size={22} />
      </span>
      <span className="flex min-w-0 flex-1 flex-col">
        <span className="truncate font-display text-lg font-semibold">
          {t("recap.banner", { month: label(recap.year, recap.month, false) })}
        </span>
        <span className="truncate text-sm text-muted-foreground">
          {t("recap.bannerNote", { duration: format.totalDuration(recap.listenedSeconds) })}
        </span>
      </span>
      <ChevronRightIcon className="shrink-0 text-muted-foreground transition-transform duration-150 ease-brand group-hover:translate-x-0.5" />
    </Link>
  );
}
