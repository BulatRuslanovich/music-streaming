"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { cn } from "@/lib/cn";
import { queries } from "@/lib/queries";
import { useFormat } from "@/lib/useFormat";
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
  const format = useFormat();
  if (days.length === 0) return null;

  const longest = Math.max(...days.map((day) => day.listenedSeconds));

  return (
    <ol className="flex h-35 items-end gap-0.5 pt-2">
      {days.map((day) => (
        <ChartColumn
          key={day.date}
          share={percent(day.listenedSeconds, longest)}
          title={`${format.shortDate(day.date)} · ${format.totalDuration(day.listenedSeconds)}`}
        />
      ))}
    </ol>
  );
}

function HourlyChart({ hours: activity }: { hours: HourlyActivity[] }) {
  const format = useFormat();

  const byHour = new Map(activity.map((entry) => [entry.hour, entry.listenedSeconds]));
  const longest = Math.max(1, ...activity.map((entry) => entry.listenedSeconds));

  return (
    <ol className="flex h-30 items-end gap-0.5 pt-2 pb-4">
      {Array.from({ length: 24 }, (_, hour) => {
        const seconds = byHour.get(hour) ?? 0;

        return (
          <ChartColumn
            key={hour}
            share={percent(seconds, longest)}
            title={`${String(hour).padStart(2, "0")}:00 · ${format.totalDuration(seconds)}`}
            tick={hour % 6 === 0 ? String(hour) : undefined}
          />
        );
      })}
    </ol>
  );
}

function ChartColumn({ share, title, tick }: { share: number; title: string; tick?: string }) {
  return (
    <li title={title} className="group relative flex h-full min-w-[3px] flex-1 items-end">
      <span
        style={{ ["--share" as string]: `${share}%` }}
        className={cn(
          "block h-(--share) min-h-0.5 w-full rounded-t-[3px] bg-primary opacity-85 transition-opacity",
          "group-hover:opacity-100",
        )}
      />
      {tick && <span className="absolute -bottom-4 left-0 text-2xs text-faint">{tick}</span>}
    </li>
  );
}

function percent(value: number, of: number): number {
  return of <= 0 ? 0 : Math.max(2, Math.round((value / of) * 100));
}
