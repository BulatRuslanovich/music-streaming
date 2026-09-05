// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { Route } from "next";
import type { ReactNode } from "react";
import { RankedRow } from "@/components/collection/RankedRow";
import { SectionHeader } from "@/components/PageHeader";
import { useFormat } from "@/lib/useFormat";
import { useT } from "@/contexts/I18nContext";
import type { StatisticsEntry } from "@/lib/types";

/**
 * Пронумерованный топ: заголовок и список строк.
 *
 * Вынесено со страницы `/statistics`, потому что теми же четырьмя топами — исполнители,
 * альбомы, жанры, треки — теперь открывается и карточка слушателя в админке. Своя копия там
 * разошлась бы с этой на первом же изменении геометрии.
 */
export function Ranked({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-3">
      <SectionHeader title={title} />
      <ol className="flex flex-col gap-0.5">{children}</ol>
    </section>
  );
}

export function RankedEntries<T extends string>({
  title,
  entries,
  href,
  art,
}: {
  title: string;
  entries: StatisticsEntry[];
  href: (entry: StatisticsEntry) => Route<T>;
  art: (entry: StatisticsEntry) => ReactNode;
}) {
  const t = useT();
  const format = useFormat();

  if (entries.length === 0) return null;

  const longest = entries[0].listenedSeconds;

  return (
    <Ranked title={title}>
      {entries.map((entry, index) => (
        <li key={entry.id}>
          <RankedRow
            rank={index + 1}
            featured={index === 0}
            title={entry.name}
            bar={rankedShare(entry.listenedSeconds, longest)}
            href={href(entry)}
            art={art(entry)}
            trailing={
              <RankedValue
                main={format.totalDuration(entry.listenedSeconds)}
                hint={t("stats.playCount", { count: entry.plays })}
              />
            }
          />
        </li>
      ))}
    </Ranked>
  );
}

export function RankedValue({ main, hint }: { main: string; hint: string }) {
  return (
    <span className="flex flex-col items-end text-sm whitespace-nowrap tabular-nums">
      {main}
      <span className="text-2xs text-muted-foreground">{hint}</span>
    </span>
  );
}

/** Доля от лидера в процентах. Минимум в два процента, иначе полоса схлопывается в невидимую. */
export function rankedShare(value: number, of: number): number {
  if (value <= 0 || of <= 0) return 0;
  return Math.max(2, Math.round((value / of) * 100));
}
