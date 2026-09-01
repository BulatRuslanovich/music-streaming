// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useT } from "@/contexts/I18nContext";

export interface ActivityPoint {
  key: string;
  label: string;
  value: number;
  plays: number;
  /** Подпись на оси. Ставится не у каждой точки — иначе они сливаются. */
  tick?: string;
}

/**
 * Текстовый дубль графика для скринридеров. Вынесен отдельно, потому что форм графика
 * теперь три (столбики, теплокарта, циферблат), а озвучивать их надо одинаково — сами
 * данные от формы не зависят.
 */
export function ActivityTable({
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

  return (
    <table className="sr-only">
      <caption>{tableLabel}</caption>
      <thead>
        <tr>
          <th scope="col">{columnLabel}</th>
          <th scope="col">{t("stats.listeningTime")}</th>
          <th scope="col">{t("stats.plays")}</th>
        </tr>
      </thead>
      <tbody>
        {points.map((point) => (
          <tr key={point.key}>
            <th scope="row">{point.label}</th>
            <td>{formatValue(point.value)}</td>
            <td>{point.plays}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** Всплывающая подпись над элементом графика. Показывается по `group-hover` родителя. */
export function ActivityTip({
  value,
  label,
  plays,
  anchor,
  formatValue,
}: {
  value: number;
  label: string;
  plays: number;
  anchor: string;
  formatValue: (seconds: number) => string;
}) {
  const t = useT();

  return (
    <span
      className={[
        "pointer-events-none absolute bottom-full z-10 mb-1.5 hidden w-max items-baseline gap-1.5",
        "rounded-lg bg-popover px-2.5 py-1 whitespace-nowrap",
        "text-popover-foreground shadow-pop group-hover:flex",
        anchor,
      ].join(" ")}
    >
      <span className="text-sm font-semibold tabular-nums">{formatValue(value)}</span>
      <span className="text-2xs text-muted-foreground">
        {label} · {t("stats.playCount", { count: plays })}
      </span>
    </span>
  );
}
