// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { cn } from "@/lib/cn";
import { queries } from "@/lib/queries";
import type { TranslationKey } from "@/lib/i18n";
import { sourceLabel } from "@/lib/sourceLabel";
import type { RankingWeights, RecommendationStats as Stats, SourceStats } from "@/lib/types";
import { useFormat } from "@/lib/useFormat";
import { useT } from "@/contexts/I18nContext";
import { Query } from "@/components/Query";
import { ToggleGroup, ToggleGroupButton } from "@/components/ui/toggle-group";

const PERIODS = [7, 30, 90];

const percent = (part: number, whole: number) =>
  whole > 0 ? Math.round((part / whole) * 100) : null;

const percentLabel = (part: number, whole: number) => {
  const value = percent(part, whole);
  return value === null ? "—" : `${value}%`;
};

// Срабатывают ли рекомендации: доля музыки из них и, по каждому источнику, показы → запуски →
// дослушали / бросили сразу → лайки.
export function RecommendationStats() {
  const t = useT();
  const [days, setDays] = useState(30);
  const stats = useQuery(queries.recommendationStats(days));

  return (
    <div className="flex flex-col gap-5">
      <section className="flex flex-col gap-4 rounded-lg bg-card p-4">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="flex max-w-[60ch] flex-col gap-1">
            <h2 className="text-section font-semibold">{t("stats.title")}</h2>
            <p className="text-sm text-muted-foreground">{t("stats.hint")}</p>
          </div>
          <ToggleGroup>
            {PERIODS.map((period) => (
              <ToggleGroupButton
                key={period}
                active={period === days}
                onClick={() => setDays(period)}
              >
                {t("stats.days", { count: period })}
              </ToggleGroupButton>
            ))}
          </ToggleGroup>
        </div>

        <Query
          result={stats}
          isEmpty={(data) => data.sources.length === 0}
          empty={{ title: t("stats.empty") }}
        >
          {(data) => <Report stats={data} />}
        </Query>
      </section>

      {stats.data && <Weights weights={stats.data.weights} />}
    </div>
  );
}

function Report({ stats }: { stats: Stats }) {
  const t = useT();
  const format = useFormat();

  const share = percent(stats.recommendedSeconds, stats.listenedSeconds) ?? 0;
  const finished = (rows: SourceStats[]) =>
    percent(
      rows.reduce((sum, row) => sum + row.completed, 0),
      rows.reduce((sum, row) => sum + row.completed + row.skipped, 0),
    );
  const recommended = finished(stats.sources.filter((row) => row.recommended));
  const rest = finished(stats.sources.filter((row) => !row.recommended));

  return (
    <>
      <div className="flex flex-col gap-2">
        <p className="font-display text-2xl font-semibold">{t("stats.share", { share })}</p>
        <div className="flex h-2 overflow-hidden rounded-full bg-raised" aria-hidden="true">
          <span className="h-full rounded-full bg-primary" style={{ width: `${share}%` }} />
        </div>
        <p className="text-sm text-muted-foreground">
          {t("stats.shareNote", {
            recommended: format.totalDuration(stats.recommendedSeconds),
            total: format.totalDuration(stats.listenedSeconds),
          })}
        </p>
        {recommended !== null && rest !== null && (
          <p className="text-sm">{t("stats.completion", { recommended, rest })}</p>
        )}
      </div>

      <div className="-mx-4 overflow-x-auto px-4">
        <table className="w-full min-w-[40rem] text-sm tabular-nums">
          <thead className="text-left text-xs text-muted-foreground">
            <tr className="[&>th]:px-2 [&>th]:py-2 [&>th]:font-medium">
              <th>{t("stats.source")}</th>
              <th className="text-right">{t("stats.impressions")}</th>
              <th className="text-right">{t("stats.starts")}</th>
              <th className="text-right">{t("stats.completed")}</th>
              <th className="text-right">{t("stats.skippedEarly")}</th>
              <th className="text-right">{t("stats.liked")}</th>
              <th className="text-right">{t("stats.time")}</th>
            </tr>
          </thead>
          <tbody>
            {stats.sources.map((row) => {
              const ended = row.completed + row.skipped;
              const conversion = percent(row.starts, row.impressions);

              return (
                <tr
                  key={row.source ?? ""}
                  className="border-t border-border [&>td]:px-2 [&>td]:py-2"
                >
                  <td>
                    <span className="flex items-center gap-2">
                      <span
                        className={cn(
                          "size-2 shrink-0 rounded-full",
                          row.recommended ? "bg-primary" : "bg-transparent",
                        )}
                        title={row.recommended ? t("stats.recommended") : undefined}
                      />
                      <span className={cn("truncate", !row.source && "text-muted-foreground")}>
                        {sourceLabel(row.source, t)}
                      </span>
                    </span>
                  </td>
                  <td className="text-right text-muted-foreground">{row.impressions || "—"}</td>
                  <td className="text-right">
                    {row.starts}
                    {conversion !== null && (
                      <span className="ml-1 text-xs text-muted-foreground">({conversion}%)</span>
                    )}
                  </td>
                  <td className="text-right">{percentLabel(row.completed, ended)}</td>
                  <td className="text-right">{percentLabel(row.skippedEarly, ended)}</td>
                  <td className="text-right">{row.liked || "—"}</td>
                  <td className="text-right text-muted-foreground">
                    {row.listenedSeconds > 0 ? format.totalDuration(row.listenedSeconds) : "—"}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </>
  );
}

// Веса признаков: стартовые (ручные) и выученные на исходах этого слушателя.
function Weights({ weights }: { weights: RankingWeights }) {
  const t = useT();
  const learned = weights.features.some((feature) => feature.learned != null);
  const widest = Math.max(
    ...weights.features.flatMap((feature) => [feature.hand, feature.learned ?? 0]),
    0.01,
  );

  return (
    <section className="flex flex-col gap-4 rounded-lg bg-card p-4">
      <div className="flex max-w-[60ch] flex-col gap-1">
        <h2 className="text-section font-semibold">{t("stats.weights")}</h2>
        <p className="text-sm text-muted-foreground">{t("stats.weightsHint")}</p>
      </div>

      <p className="text-sm">
        {learned
          ? t("stats.weightsShare", {
              examples: weights.examples,
              share: Math.round(weights.share * 100),
            })
          : t("stats.weightsWaiting", {
              examples: weights.examples,
              needed: weights.minimumExamples,
            })}
      </p>

      <ul className="flex flex-col gap-3">
        {weights.features.map((feature) => (
          <li
            key={feature.name}
            className="grid grid-cols-[minmax(0,14rem)_minmax(0,1fr)] items-center gap-x-4 gap-y-1 text-sm max-sm:grid-cols-1"
          >
            <span className="truncate">{t(`stats.feature.${feature.name}` as TranslationKey)}</span>
            <span className="flex flex-col gap-1">
              <Bar value={feature.hand} widest={widest} label={t("stats.weightHand")} muted />
              {learned && (
                <Bar
                  value={feature.learned ?? 0}
                  widest={widest}
                  label={t("stats.weightLearned")}
                />
              )}
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}

function Bar({
  value,
  widest,
  label,
  muted = false,
}: {
  value: number;
  widest: number;
  label: string;
  muted?: boolean;
}) {
  return (
    <span className="grid grid-cols-[minmax(0,1fr)_3rem] items-center gap-3" title={label}>
      <span className="h-2 overflow-hidden rounded-full bg-raised">
        <span
          className={cn("block h-full rounded-full", muted ? "bg-foreground/30" : "bg-primary")}
          style={{ width: `${(value / widest) * 100}%` }}
        />
      </span>
      <span className="text-right text-xs text-muted-foreground tabular-nums">
        {Math.round(value * 100)}%
      </span>
    </span>
  );
}
