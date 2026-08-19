// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { queries } from "@/lib/queries";
import { useFormat } from "@/lib/useFormat";
import { ActivityChart, type ActivityPoint } from "@/components/ActivityChart";
import { Cover } from "@/components/Cover";
import { PageHeader, SectionHeader } from "@/components/PageHeader";
import { Query } from "@/components/Query";
import { Surface } from "@/components/ui/card";
import { Overline } from "@/components/ui/label";
import { ToggleGroup, ToggleGroupButton } from "@/components/ui/tabs";
import { usePlayer } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import type { DailyActivity, HourlyActivity, StatisticsEntry, StatisticsPeriod } from "@/lib/types";

const PERIODS: StatisticsPeriod[] = ["Week", "Month", "Quarter", "Year", "All"];

export default function StatisticsPage() {
  const t = useT();
  const format = useFormat();
  const player = usePlayer();

  const [period, setPeriod] = useState<StatisticsPeriod>("Month");
  const statistics = useQuery(queries.statistics(period));

  return (
    <>
      <PageHeader title={t("stats.title")} />

      <ToggleGroup aria-label={t("stats.periodLabel")}>
        {PERIODS.map((value) => (
          <ToggleGroupButton key={value} active={value === period} onClick={() => setPeriod(value)}>
            {t(`stats.period.${value}` as const)}
          </ToggleGroupButton>
        ))}
      </ToggleGroup>

      <Query
        result={statistics}
        skeleton="tile"
        isEmpty={(data) => data.summary.plays === 0}
        empty={{ title: t("stats.empty") }}
      >
        {(data) => (
          <>
            <div className="grid grid-cols-[repeat(auto-fill,minmax(10.25rem,1fr))] gap-4">
              <Tile
                label={t("stats.listeningTime")}
                value={format.totalDuration(data.summary.listenedSeconds)}
              />
              <Tile label={t("stats.plays")} value={String(data.summary.plays)} />
              <Tile label={t("stats.uniqueTracks")} value={String(data.summary.uniqueTracks)} />
              <Tile label={t("stats.uniqueArtists")} value={String(data.summary.uniqueArtists)} />
              <Tile label={t("stats.uniqueAlbums")} value={String(data.summary.uniqueAlbums)} />
              <Tile label={t("stats.activeDays")} value={String(data.summary.activeDays)} />
              {data.summary.peakDay && (
                <Tile
                  label={t("stats.peakDay")}
                  value={format.shortDate(data.summary.peakDay.date)}
                  hint={format.totalDuration(data.summary.peakDay.listenedSeconds)}
                />
              )}
              {data.summary.peakHour && (
                <Tile
                  label={t("stats.peakHour")}
                  value={`${String(data.summary.peakHour.hour).padStart(2, "0")}:00`}
                  hint={format.totalDuration(data.summary.peakHour.listenedSeconds)}
                />
              )}
            </div>

            <section className="flex flex-col gap-3">
              <SectionHeader title={t("stats.byDay")} />
              <DailyChart days={data.byDay} />
            </section>

            <section className="flex flex-col gap-3">
              <SectionHeader title={t("stats.byHour")} />
              <HourlyChart hours={data.byHour} />
            </section>

            {data.topTracks.length > 0 && (
              <section className="flex flex-col gap-3">
                <SectionHeader title={t("stats.topTracks")} />
                <ol className="flex flex-col">
                  {data.topTracks.map((entry, index) => (
                    <li key={entry.track.id}>
                      <button
                        type="button"
                        onClick={() =>
                          player.playQueue(
                            data.topTracks.map((item) => item.track),
                            index,
                            { source: "history" },
                          )
                        }
                        className="grid w-full grid-cols-[1.5rem_auto_minmax(0,1fr)_auto] items-center gap-3 rounded-md px-2 py-1.5 text-left transition-colors hover:bg-card"
                      >
                        <Rank>{index + 1}</Rank>
                        <Cover
                          albumId={entry.track.albumId}
                          trackId={entry.track.id}
                          hasCover={entry.track.hasCover}
                          name={entry.track.albumTitle ?? entry.track.title}
                          size={40}
                        />
                        <span className="flex min-w-0 flex-col">
                          <span className="truncate font-semibold">{entry.track.title}</span>
                          <span className="truncate text-xs text-muted-foreground">
                            {entry.track.artistName}
                          </span>
                        </span>
                        <Value
                          main={format.totalDuration(entry.listenedSeconds)}
                          hint={t("stats.playCount", { count: entry.plays })}
                        />
                      </button>
                    </li>
                  ))}
                </ol>
              </section>
            )}

            <Ranked title={t("stats.topArtists")} entries={data.topArtists} />
            <Ranked title={t("stats.topAlbums")} entries={data.topAlbums} />
            <Ranked title={t("stats.topGenres")} entries={data.topGenres} />
          </>
        )}
      </Query>
    </>
  );
}

function Rank({ children }: { children: React.ReactNode }) {
  return <span className="text-right text-sm font-bold text-faint tabular-nums">{children}</span>;
}

function Value({ main, hint }: { main: string; hint: string }) {
  return (
    <span className="flex flex-col items-end text-sm whitespace-nowrap tabular-nums">
      {main}
      <span className="text-2xs text-muted-foreground">{hint}</span>
    </span>
  );
}

function Tile({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <Surface variant="tile" padding="sm" className="flex flex-col gap-1">
      <Overline>{label}</Overline>
      <strong className="text-2xl leading-tight">{value}</strong>
      {hint && <span className="text-2xs text-muted-foreground">{hint}</span>}
    </Surface>
  );
}

function Ranked({ title, entries }: { title: string; entries: StatisticsEntry[] }) {
  const t = useT();
  const format = useFormat();

  if (entries.length === 0) return null;

  const longest = Math.max(...entries.map((entry) => entry.listenedSeconds));

  return (
    <section className="flex flex-col gap-3">
      <SectionHeader title={title} />
      <ol className="flex flex-col gap-2">
        {entries.map((entry, index) => (
          <li
            key={entry.id}
            className="grid grid-cols-[1.5rem_minmax(0,8.75rem)_minmax(0,1fr)_auto] items-center gap-3 max-[700px]:grid-cols-[1.25rem_minmax(0,1fr)_auto]"
          >
            <Rank>{index + 1}</Rank>
            <span className="truncate text-sm font-semibold">{entry.name}</span>
            <span
              aria-hidden="true"
              className="h-2 rounded-full bg-raised max-[700px]:hidden"
              style={{ ["--share" as string]: `${percent(entry.listenedSeconds, longest)}%` }}
            >
              <span className="block h-full w-(--share) rounded-full bg-primary" />
            </span>
            <Value
              main={format.totalDuration(entry.listenedSeconds)}
              hint={t("stats.playCount", { count: entry.plays })}
            />
          </li>
        ))}
      </ol>
    </section>
  );
}

function DailyChart({ days }: { days: DailyActivity[] }) {
  const t = useT();
  const format = useFormat();

  if (days.length === 0) return null;

  const every = Math.max(1, Math.ceil(days.length / 5));

  const points: ActivityPoint[] = days.map((day, index) => ({
    key: day.date,
    label: format.shortDate(day.date),
    value: day.listenedSeconds,
    plays: day.plays,
    tick: index % every === 0 ? format.shortDate(day.date) : undefined,
  }));

  return (
    <ActivityChart
      points={points}
      columnLabel={t("stats.date")}
      tableLabel={t("stats.byDay")}
      formatValue={format.totalDuration}
    />
  );
}

function HourlyChart({ hours: activity }: { hours: HourlyActivity[] }) {
  const t = useT();
  const format = useFormat();
  const byHour = new Map(activity.map((entry) => [entry.hour, entry]));

  const points: ActivityPoint[] = Array.from({ length: 24 }, (_, hour) => {
    const entry = byHour.get(hour);
    const label = `${String(hour).padStart(2, "0")}:00`;

    return {
      key: label,
      label,
      value: entry?.listenedSeconds ?? 0,
      plays: entry?.plays ?? 0,
      tick: hour % 6 === 0 ? label : undefined,
    };
  });

  return (
    <ActivityChart
      points={points}
      columnLabel={t("stats.hour")}
      tableLabel={t("stats.byHour")}
      formatValue={format.totalDuration}
    />
  );
}

function percent(value: number, of: number): number {
  if (value <= 0 || of <= 0) return 0;
  return Math.max(2, Math.round((value / of) * 100));
}
