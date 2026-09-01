// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { cn } from "@/lib/cn";
import { heightOf, scaleFor, tickLabel } from "@/lib/activityScale";
import { useT } from "@/contexts/I18nContext";
import { ActivityTable, ActivityTip, type ActivityPoint } from "./ActivityTable";

export type { ActivityPoint } from "./ActivityTable";

export function ActivityChart({
  points,
  columnLabel,
  tableLabel,
  formatValue,
}: {
  points: ActivityPoint[];
  columnLabel: string;
  tableLabel: string;
  formatValue: (seconds: number) => string;
}) {
  const t = useT();

  if (points.length === 0) return null;

  const { top, ticks } = scaleFor(Math.max(...points.map((point) => point.value)));

  // Ширина столбика зависит от того, сколько их: у недели фиксированные 24px оставляли
  // между столбиками по полтора экрана пустоты, у месяца те же 24px в самый раз.
  const barWidth = points.length <= 10 ? "max-w-16" : points.length <= 31 ? "max-w-8" : "max-w-6";

  return (
    <figure className="m-0 flex flex-col">
      <div className="grid grid-cols-[4rem_minmax(0,1fr)] gap-x-2 pt-10">
        <div aria-hidden="true" className="relative h-40">
          {ticks.map((tick) => (
            <span
              key={tick}
              style={{ bottom: `${(tick / top) * 100}%` }}
              className="absolute right-0 translate-y-1/2 text-2xs whitespace-nowrap text-faint tabular-nums"
            >
              {tickLabel(tick, t)}
            </span>
          ))}
        </div>

        <div aria-hidden="true" className="relative h-40">
          {ticks.map((tick) => (
            <span
              key={tick}
              style={{ bottom: `${(tick / top) * 100}%` }}
              className={cn(
                "absolute inset-x-0 h-px",
                tick === 0 ? "bg-border-strong" : "bg-border",
              )}
            />
          ))}

          <ol className="absolute inset-0 flex items-end">
            {points.map((point, index) => (
              <li key={point.key} className="group relative flex h-full flex-1 items-end px-px">
                <span
                  style={{ height: `${heightOf(point.value, top)}%` }}
                  className="relative flex w-full justify-center"
                >
                  <span
                    className={cn(
                      "block h-full w-full rounded-t-[4px] bg-primary opacity-80 transition-opacity",
                      barWidth,
                      "group-hover:opacity-100",
                      point.value > 0 && "min-h-0.5",
                    )}
                  />

                  <ActivityTip
                    value={point.value}
                    label={point.label}
                    plays={point.plays}
                    formatValue={formatValue}
                    anchor={anchorFor(index, points.length)}
                  />
                </span>
              </li>
            ))}
          </ol>
        </div>

        <div />

        <ol aria-hidden="true" className="flex pt-1.5">
          {points.map((point) => (
            <li key={point.key} className="relative h-4 min-w-px flex-1">
              {point.tick && (
                <span className="absolute left-1/2 -translate-x-1/2 text-2xs whitespace-nowrap text-faint tabular-nums">
                  {point.tick}
                </span>
              )}
            </li>
          ))}
        </ol>
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

function anchorFor(index: number, count: number): string {
  if (count <= 2) return "left-1/2 -translate-x-1/2";
  if (index <= (count - 1) * 0.15) return "left-0";
  if (index >= (count - 1) * 0.85) return "right-0";
  return "left-1/2 -translate-x-1/2";
}
