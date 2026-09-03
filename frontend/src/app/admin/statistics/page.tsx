// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { Suspense, useCallback } from "react";
import { queries } from "@/lib/queries";
import { dailyPoints, DENSE_FROM, densifyDays } from "@/lib/activityScale";
import { parsePeriod, percent, uploadPoints } from "@/lib/adminStatistics";
import { useFormat } from "@/lib/useFormat";
import { ActivityChart } from "@/components/ActivityChart";
import { ActivityHeatmap } from "@/components/ActivityHeatmap";
import { PeriodTabs, useUrlFilters } from "@/components/admin/AdminFilters";
import { StatGrid } from "@/components/admin/StatGrid";
import { ChartIcon } from "@/components/Icons";
import { PageHeader, SectionHeader } from "@/components/PageHeader";
import { Query } from "@/components/Query";
import { Surface } from "@/components/ui/card";
import { Overline } from "@/components/ui/label";
import { useT } from "@/contexts/I18nContext";
import type { TranslationKey } from "@/lib/i18n";
import type { AdminListener, AdminOverview, StatisticsPeriod } from "@/lib/types";

const RANKING_SIZE = 5;

export default function AdminStatisticsPage() {
  const t = useT();

  return (
    <Suspense fallback={<PageHeader title={t("admin.stats.title")} />}>
      <AdminStatisticsView />
    </Suspense>
  );
}

function AdminStatisticsView() {
  const t = useT();
  const { params, set } = useUrlFilters("/admin/statistics");

  const period = parsePeriod(params.get("period"), "Month");
  const setPeriod = useCallback(
    (next: StatisticsPeriod) => set({ period: next === "Month" ? undefined : next }),
    [set],
  );

  const overview = useQuery(queries.adminOverview(period));

  return (
    <>
      <PageHeader title={t("admin.stats.title")} subtitle={t("admin.stats.subtitle")} />

      <PeriodTabs period={period} onChange={setPeriod} />

      <Query
        result={overview}
        skeleton="tile"
        skeletonCount={3}
        isEmpty={(data) => data.library.totalTracks === 0 && data.users.total === 0}
        empty={{ icon: <ChartIcon size={24} />, title: t("admin.stats.empty") }}
      >
        {(data) => (
          <div className="flex flex-col gap-8">
            <Totals data={data} />
            <Charts data={data} />
            <Rankings period={period} />
            <CatalogHealth />
          </div>
        )}
      </Query>
    </>
  );
}

function Totals({ data }: { data: AdminOverview }) {
  const t = useT();
  const format = useFormat();

  return (
    <div className="flex flex-col gap-4">
      <StatGrid
        title={t("admin.stats.people")}
        stats={[
          { label: t("admin.stats.totalUsers"), value: String(data.users.total) },
          {
            label: t("admin.stats.activeUsers"),
            value: String(data.users.active),
            hint: t("admin.stats.activeUsersHint"),
          },
          { label: t("admin.stats.newUsers"), value: String(data.users.new) },
          {
            label: t("admin.stats.listeners"),
            value: String(data.listening.uniqueListeners),
          },
        ]}
      />

      <StatGrid
        title={t("admin.stats.library")}
        stats={[
          { label: t("admin.stats.totalTracks"), value: String(data.library.totalTracks) },
          {
            label: t("admin.stats.addedTracks"),
            value: String(data.library.tracksAddedInPeriod),
          },
          { label: t("admin.stats.totalSize"), value: format.bytes(data.library.totalBytes) },
          {
            label: t("admin.stats.totalDuration"),
            value: format.totalDuration(data.library.totalDurationSeconds),
          },
        ]}
      >
        <IngestionBreakdown data={data} />
      </StatGrid>

      <StatGrid
        title={t("admin.stats.listening")}
        stats={[
          {
            label: t("admin.stats.listenedTime"),
            value: format.totalDuration(data.listening.listenedSeconds),
          },
          { label: t("admin.stats.plays"), value: String(data.listening.plays) },
          { label: t("admin.stats.tracksHeard"), value: String(data.listening.uniqueTracks) },
          {
            label: t("admin.stats.skipRate"),
            value: `${percent(data.listening.skipRate)}%`,
            hint: `${data.listening.completed} / ${data.listening.skipped}`,
          },
        ]}
      />
    </div>
  );
}

function IngestionBreakdown({ data }: { data: AdminOverview }) {
  const t = useT();

  const total = data.uploadsBySource.reduce((sum, entry) => sum + entry.tracks, 0);

  return (
    <div className="flex flex-col gap-2">
      <Overline>{t("admin.stats.bySource")}</Overline>
      <ul className="flex flex-col gap-1.5">
        {data.uploadsBySource.map((entry) => (
          <li key={entry.source} className="flex items-center gap-3 text-sm">
            <span className="w-40 shrink-0 truncate text-muted-foreground max-md:w-28">
              {t(`admin.stats.source.${entry.source}` as TranslationKey)}
            </span>
            <span
              aria-hidden="true"
              className="h-1.5 min-w-0.5 rounded-full bg-primary"
              style={{ width: `${total === 0 ? 0 : (entry.tracks / total) * 100}%` }}
            />
            <span className="tabular-nums">{entry.tracks}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function Charts({ data }: { data: AdminOverview }) {
  const t = useT();
  const format = useFormat();

  const days = densifyDays(data.activityByDay, data.from);

  return (
    <div className="flex flex-col gap-8">
      <section className="flex flex-col gap-3">
        <SectionHeader title={t("admin.stats.activity")} />
        {days.length > DENSE_FROM ? (
          <ActivityHeatmap
            days={days}
            columnLabel={t("stats.date")}
            tableLabel={t("admin.stats.activity")}
            formatValue={format.totalDuration}
          />
        ) : (
          <ActivityChart
            points={dailyPoints(days, format.shortDate)}
            columnLabel={t("stats.date")}
            tableLabel={t("admin.stats.activity")}
            formatValue={format.totalDuration}
          />
        )}
      </section>

      <section className="flex flex-col gap-3">
        <SectionHeader title={t("admin.stats.uploadsPerDay")} />
        <ActivityChart
          points={uploadPoints(data.uploadsByDay, format.shortDate)}
          columnLabel={t("stats.date")}
          tableLabel={t("admin.stats.uploadsPerDay")}
          formatValue={(tracks) => String(tracks)}
        />
      </section>
    </div>
  );
}

function Rankings({ period }: { period: StatisticsPeriod }) {
  const t = useT();

  return (
    <div className="grid grid-cols-2 gap-6 max-md:grid-cols-1">
      <Ranking
        title={t("admin.stats.topListeners")}
        period={period}
        sort="ListenedSeconds"
        render={(listener, format) => format.totalDuration(listener.listenedSeconds)}
      />
      <Ranking
        title={t("admin.stats.topUploaders")}
        period={period}
        sort="UploadedTracks"
        render={(listener) => String(listener.uploadedTracks)}
      />
    </div>
  );
}

function Ranking({
  title,
  period,
  sort,
  render,
}: {
  title: string;
  period: StatisticsPeriod;
  sort: "ListenedSeconds" | "UploadedTracks";
  render: (listener: AdminListener, format: ReturnType<typeof useFormat>) => string;
}) {
  const t = useT();
  const format = useFormat();

  const listeners = useQuery(
    queries.adminListeners({ period, sort, direction: "Desc", page: 1, pageSize: RANKING_SIZE }),
  );

  return (
    <section className="flex flex-col gap-3">
      <SectionHeader title={title} />
      <Query
        result={listeners}
        skeleton="row"
        skeletonCount={RANKING_SIZE}
        empty={{ title: t("admin.stats.listenersEmpty") }}
      >
        {(data) => (
          <ul className="flex flex-col gap-1">
            {data.items.map((listener, index) => (
              <li key={listener.id}>
                <Link
                  href={`/admin/statistics/users/${listener.id}`}
                  className="flex items-center gap-3 rounded-lg px-2 py-2 text-sm hover:bg-accent"
                >
                  <span className="w-4 shrink-0 text-faint tabular-nums">{index + 1}</span>
                  <span className="min-w-0 flex-1 truncate">{listener.username}</span>
                  <span className="shrink-0 tabular-nums text-muted-foreground">
                    {render(listener, format)}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </Query>
    </section>
  );
}

function CatalogHealth() {
  const t = useT();
  const health = useQuery(queries.adminCatalogHealth());

  return (
    <section className="flex flex-col gap-3">
      <SectionHeader title={t("admin.stats.catalog")} />
      <Query result={health} skeleton="tile" skeletonCount={1}>
        {(data) => (
          <Surface variant="tile" padding="lg" className="flex flex-col gap-4">
            <dl className="grid grid-cols-4 gap-4 max-md:grid-cols-2">
              {(
                [
                  ["admin.stats.withoutCover", data.withoutCover],
                  ["admin.stats.withoutLyrics", data.withoutLyrics],
                  ["admin.stats.withoutGenre", data.withoutGenre],
                  ["admin.stats.withoutAlbum", data.withoutAlbum],
                  ["admin.stats.withoutYear", data.withoutYear],
                  ["admin.stats.neverListened", data.neverListened],
                  ["admin.stats.highSkipRate", data.highSkipRate],
                ] as [TranslationKey, number][]
              ).map(([key, value]) => (
                <div key={key} className="min-w-0">
                  <dt className="text-xs text-muted-foreground">{t(key)}</dt>
                  <dd className="mt-0.5 text-lg font-semibold tabular-nums">
                    {value}
                    <span className="ml-1 text-xs font-normal text-faint">
                      / {data.totalTracks}
                    </span>
                  </dd>
                </div>
              ))}
            </dl>

            <p className="text-2xs text-faint">
              {t("admin.stats.highSkipRateHint", {
                percent: percent(data.highSkipRateThreshold),
                events: data.highSkipRateMinimumEvents,
              })}
            </p>
          </Surface>
        )}
      </Query>
    </section>
  );
}
