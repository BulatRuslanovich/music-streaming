// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ReactNode } from "react";
import { Surface } from "@/components/ui/card";
import { Overline } from "@/components/ui/label";

export interface Stat {
  label: string;
  value: string;
  hint?: string;
}

/**
 * Блок показателей. Сетка сжимается до двух колонок на телефоне: четыре числа в строку там
 * переносятся по букве и читаются хуже, чем в два ряда.
 */
export function StatGrid({
  title,
  stats,
  children,
}: {
  title: string;
  stats: Stat[];
  children?: ReactNode;
}) {
  return (
    <Surface variant="tile" padding="lg" className="flex flex-col gap-4">
      <Overline>{title}</Overline>

      <dl className="grid grid-cols-4 gap-4 max-md:grid-cols-2">
        {stats.map((stat) => (
          <div key={stat.label} className="min-w-0">
            <dt className="text-xs text-muted-foreground">{stat.label}</dt>
            <dd className="mt-0.5 truncate text-lg font-semibold tabular-nums">{stat.value}</dd>
            {stat.hint && <p className="mt-0.5 text-2xs text-faint">{stat.hint}</p>}
          </div>
        ))}
      </dl>

      {children}
    </Surface>
  );
}
