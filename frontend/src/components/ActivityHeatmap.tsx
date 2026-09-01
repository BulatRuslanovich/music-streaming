// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMemo } from "react";
import { cn } from "@/lib/cn";
import { intensityOf, parseLocalDate, weekdayIndex } from "@/lib/activityScale";
import { useFormat } from "@/lib/useFormat";
import { useI18n, useT } from "@/contexts/I18nContext";
import type { DailyActivity } from "@/lib/types";
import { ActivityTable, ActivityTip } from "./ActivityTable";

/**
 * Календарь вместо столбиков на длинных периодах. Год — это 365 столбиков уже́ пикселя:
 * форма, в которой нельзя ни прочитать отдельный день, ни увидеть ритм недели. Сетка
 * «недели по колонкам, дни недели по строкам» решает обе задачи одной раскладкой.
 */

const CELL = "size-3 rounded-[3px] max-md:size-2.5";

// Ступени берут один и тот же --primary: цвет остаётся продуктовым, меняется только вес.
const LEVELS = [
  "bg-raised",
  "bg-primary/25",
  "bg-primary/45",
  "bg-primary/70",
  "bg-primary",
] as const;

interface Cell {
  date: string;
  value: number;
  plays: number;
}

export function ActivityHeatmap({
  days,
  columnLabel,
  tableLabel,
  formatValue,
}: {
  days: DailyActivity[];
  columnLabel: string;
  tableLabel: string;
  formatValue: (seconds: number) => string;
}) {
  const t = useT();
  const { locale } = useI18n();
  const format = useFormat();

  const { weeks, months } = useMemo(() => buildCalendar(days, locale), [days, locale]);

  if (days.length === 0) return null;

  const max = Math.max(...days.map((day) => day.listenedSeconds));

  const points = days.map((day) => ({
    key: day.date,
    label: format.shortDate(day.date),
    value: day.listenedSeconds,
    plays: day.plays,
  }));

  const weekdayNames = weekdayLabels(locale);

  return (
    <figure className="m-0 flex flex-col gap-2">
      <div className="overflow-x-auto pb-1">
        <div aria-hidden="true" className="flex w-max gap-1.5">
          <div className="flex flex-col gap-1 pt-5">
            {weekdayNames.map((name, row) => (
              <span
                key={name}
                className={cn(
                  "flex h-3 items-center text-2xs text-faint max-md:h-2.5",
                  // Подписываем через одну: семь подряд в три буквы превращаются в кашу.
                  row % 2 === 1 && "invisible",
                )}
              >
                {name}
              </span>
            ))}
          </div>

          <div className="flex flex-col gap-1">
            <div className="flex h-4 gap-1">
              {weeks.map((week, index) => (
                <span key={week.key} className="w-3 max-md:w-2.5">
                  {months.get(index) && (
                    <span className="text-2xs whitespace-nowrap text-faint">
                      {months.get(index)}
                    </span>
                  )}
                </span>
              ))}
            </div>

            <div className="flex gap-1">
              {weeks.map((week) => (
                <div key={week.key} className="flex flex-col gap-1">
                  {week.cells.map((cell, row) =>
                    cell === null ? (
                      <span key={row} className={CELL} />
                    ) : (
                      <span key={row} className={cn("group relative", CELL)}>
                        <span
                          className={cn(
                            "block size-full rounded-[3px] transition-transform duration-150 ease-brand",
                            "group-hover:scale-125",
                            LEVELS[intensityOf(cell.value, max)],
                          )}
                        />

                        <ActivityTip
                          value={cell.value}
                          label={format.shortDate(cell.date)}
                          plays={cell.plays}
                          formatValue={formatValue}
                          anchor="left-1/2 -translate-x-1/2"
                        />
                      </span>
                    ),
                  )}
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>

      <div aria-hidden="true" className="flex items-center gap-1.5 text-2xs text-faint">
        <span>{t("stats.legendLess")}</span>
        {LEVELS.map((level) => (
          <span key={level} className={cn(CELL, level)} />
        ))}
        <span>{t("stats.legendMore")}</span>
      </div>

      <ActivityTable
        points={points}
        columnLabel={columnLabel}
        tableLabel={tableLabel}
        formatValue={formatValue}
      />
    </figure>
  );
}

/**
 * Раскладывает дни по неделям. Каждая клетка ставится на своё место по дню недели, а не
 * подряд: строка сетки обязана означать один и тот же день недели по всей ширине, даже
 * если в данных попался пропуск.
 */
function buildCalendar(days: DailyActivity[], locale: string) {
  const weeks: { key: string; cells: (Cell | null)[] }[] = [];
  const months = new Map<number, string>();

  if (days.length === 0) return { weeks, months };

  const emptyWeek = (): (Cell | null)[] => Array.from({ length: 7 }, () => null);

  let current = emptyWeek();
  let started = false;
  let weekKey = days[0].date;
  let lastMonth = -1;

  for (const day of days) {
    const date = parseLocalDate(day.date);
    const weekday = weekdayIndex(date);

    if (weekday === 0 && started) {
      weeks.push({ key: weekKey, cells: current });
      current = emptyWeek();
      weekKey = day.date;
    }

    // Метку месяца ставим над той неделей, в которой месяц сменился.
    if (date.getMonth() !== lastMonth) {
      lastMonth = date.getMonth();
      months.set(weeks.length, date.toLocaleDateString(locale, { month: "short" }));
    }

    current[weekday] = { date: day.date, value: day.listenedSeconds, plays: day.plays };
    started = true;
  }

  weeks.push({ key: weekKey, cells: current });

  // Первая метка почти всегда попадает на обрезанную неделю и висит криво — она лишняя.
  if (months.has(0) && weeks[0]?.cells[0] === null) months.delete(0);

  return { weeks, months };
}

function weekdayLabels(locale: string): string[] {
  // 2024-01-01 — понедельник, поэтому неделя набирается прямо от него.
  return Array.from({ length: 7 }, (_, index) =>
    new Date(2024, 0, 1 + index).toLocaleDateString(locale, { weekday: "short" }),
  );
}
