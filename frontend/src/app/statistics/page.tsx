// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { ChartIcon } from "@/components/Icons";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback } from "react";
import { queries } from "@/lib/queries";
import { dailyPoints, DENSE_FROM, densifyDays } from "@/lib/activityScale";
import { comparisonPeriod, periodDelta, type PeriodDelta } from "@/lib/statisticsDelta";
import { useFormat } from "@/lib/useFormat";
import { ActivityChart } from "@/components/ActivityChart";
import { ActivityHeatmap } from "@/components/ActivityHeatmap";
import { HourClock } from "@/components/HourClock";
import {
  Ranked,
  RankedEntries,
  RankedValue,
  rankedShare,
} from "@/components/collection/RankedEntries";
import { RankedRow } from "@/components/collection/RankedRow";
import { AlbumCover, ArtistCover, Cover, TrackCover } from "@/components/Cover";
import { PageHeader, SectionHeader } from "@/components/PageHeader";
import { Query } from "@/components/Query";
import { Surface } from "@/components/ui/card";
import { Overline } from "@/components/ui/label";
import { ToggleGroup, ToggleGroupButton } from "@/components/ui/tabs";
import { usePlayerActions } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import type { Statistics, StatisticsPeriod } from "@/lib/types";

const PERIODS: StatisticsPeriod[] = ["Week", "Month", "Quarter", "Year", "All"];

const DEFAULT_PERIOD: StatisticsPeriod = "Month";

function isPeriod(value: string | null): value is StatisticsPeriod {
  return value !== null && (PERIODS as string[]).includes(value);
}

export default function StatisticsPage() {
  const t = useT();

  return (
    <Suspense fallback={<PageHeader title={t("stats.title")} />}>
      <StatisticsView />
    </Suspense>
  );
}

/**
 * Период живёт в адресе, а не в состоянии: иначе на статистику нельзя дать ссылку, а
 * «назад» в браузере уносит со страницы вместо возврата к прошлому периоду. На `/search`
 * и `/genres` это уже сделано так же.
 */
function StatisticsView() {
  const t = useT();
  const router = useRouter();
  const params = useSearchParams();

  const raw = params.get("period");
  const period = isPeriod(raw) ? raw : DEFAULT_PERIOD;

  const setPeriod = useCallback(
    (next: StatisticsPeriod) => {
      router.replace(next === DEFAULT_PERIOD ? "/statistics" : `/statistics?period=${next}`);
    },
    [router],
  );

  const statistics = useQuery(queries.statistics(period));

  // Период на ступень шире нужен только ради сравнения с прошлым окном. Запрос тот же
  // самый, что и при переключении тумблера, поэтому обычно он уже лежит в кэше.
  const wider = comparisonPeriod(period);
  const comparison = useQuery({ ...queries.statistics(wider ?? period), enabled: wider !== null });

  const delta =
    wider !== null && comparison.data ? periodDelta(period, comparison.data.byDay) : null;

  return (
    <>
      <PageHeader
        title={t("stats.title")}
        actions={
          <Link href="/recap" className="font-medium text-primary hover:underline">
            {t("recap.title")} →
          </Link>
        }
      />

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
        empty={{ icon: <ChartIcon size={24} />, title: t("stats.empty") }}
      >
        {(data) => (
          <>
            <Summary data={data} period={period} delta={delta} />
            <Charts data={data} />
            <Tops data={data} />
          </>
        )}
      </Query>
    </>
  );
}

function Summary({
  data,
  period,
  delta,
}: {
  data: Statistics;
  period: StatisticsPeriod;
  delta: PeriodDelta | null;
}) {
  const t = useT();
  const format = useFormat();

  const { summary } = data;

  const facts: { label: string; value: string }[] = [
    { label: t("stats.uniqueTracks"), value: String(summary.uniqueTracks) },
    { label: t("stats.uniqueArtists"), value: String(summary.uniqueArtists) },
    { label: t("stats.uniqueAlbums"), value: String(summary.uniqueAlbums) },
    { label: t("stats.activeDays"), value: String(summary.activeDays) },
  ];

  return (
    <Surface variant="tile" padding="lg" className="flex flex-col gap-5">
      <div className="flex flex-wrap items-end justify-between gap-x-6 gap-y-3">
        <div className="min-w-0">
          <Overline>{t("stats.listeningTime")}</Overline>
          <p className="mt-1 text-display font-bold tabular-nums">
            {format.totalDuration(summary.listenedSeconds)}
          </p>
          <p className="mt-2 text-sm text-muted-foreground">
            {t("stats.playCount", { count: summary.plays })}
          </p>
        </div>

        {delta && <Delta delta={delta} period={period} />}
      </div>

      <dl className="grid grid-cols-4 gap-4 border-t border-border pt-4 max-md:grid-cols-2">
        {facts.map((fact) => (
          <div key={fact.label} className="flex flex-col gap-0.5">
            <dt className="text-2xs font-bold tracking-[0.08em] text-faint uppercase">
              {fact.label}
            </dt>
            <dd className="text-xl font-semibold tabular-nums">{fact.value}</dd>
          </div>
        ))}
      </dl>
    </Surface>
  );
}

function Delta({ delta, period }: { delta: PeriodDelta; period: StatisticsPeriod }) {
  const t = useT();
  const format = useFormat();

  const grew = delta.percent >= 0;

  return (
    <div className="flex flex-col items-end gap-0.5 text-right">
      <span
        className={
          grew
            ? "text-section font-semibold text-primary tabular-nums"
            : "text-section font-semibold text-muted-foreground tabular-nums"
        }
      >
        {grew ? "+" : "−"}
        {Math.abs(delta.percent)}%
      </span>
      <span className="text-2xs text-faint">
        {t("stats.versusPrevious", { period: t(`stats.previous.${period}` as const) })}
      </span>
      <span className="text-2xs text-faint tabular-nums">
        {format.totalDuration(delta.previous)}
      </span>
    </div>
  );
}

function Charts({ data }: { data: Statistics }) {
  const t = useT();
  const format = useFormat();

  // Разреженный ответ сервера превращаем в непрерывную ось: без этого месяц с тремя
  // активными днями рисуется тремя столбиками вплотную, как будто слушали три дня подряд.
  const days = densifyDays(data.byDay, data.from);

  return (
    <>
      <section className="flex flex-col gap-3">
        <SectionHeader title={t("stats.byDay")} />
        {data.summary.peakDay && (
          <p className="-mt-1 text-sm text-muted-foreground">
            {t("stats.peakDayIs", {
              date: format.shortDate(data.summary.peakDay.date),
              duration: format.totalDuration(data.summary.peakDay.listenedSeconds),
            })}
          </p>
        )}
        {days.length > DENSE_FROM ? (
          <ActivityHeatmap
            days={days}
            columnLabel={t("stats.date")}
            tableLabel={t("stats.byDay")}
            formatValue={format.totalDuration}
          />
        ) : (
          <ActivityChart
            points={dailyPoints(days, format.shortDate)}
            columnLabel={t("stats.date")}
            tableLabel={t("stats.byDay")}
            formatValue={format.totalDuration}
          />
        )}
      </section>

      <section className="flex flex-col gap-3">
        <SectionHeader title={t("stats.byHour")} />
        <HourClock
          hours={data.byHour}
          columnLabel={t("stats.hour")}
          tableLabel={t("stats.byHour")}
          formatValue={format.totalDuration}
        />
      </section>
    </>
  );
}

function Tops({ data }: { data: Statistics }) {
  const t = useT();
  const format = useFormat();
  const player = usePlayerActions();

  return (
    <>
      {data.topTracks.length > 0 && (
        <Ranked title={t("stats.topTracks")}>
          {data.topTracks.map((entry, index) => (
            <li key={entry.track.id}>
              <RankedRow
                rank={index + 1}
                featured={index === 0}
                title={entry.track.title}
                subtitle={entry.track.artistName}
                bar={rankedShare(entry.listenedSeconds, data.topTracks[0].listenedSeconds)}
                art={<TrackCover track={entry.track} className="size-full rounded-none" />}
                onClick={() =>
                  player.playQueue(
                    data.topTracks.map((item) => item.track),
                    index,
                    { source: "history" },
                  )
                }
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
      )}

      <RankedEntries
        title={t("stats.topArtists")}
        entries={data.topArtists}
        href={(entry) => `/artists/${entry.id}`}
        art={(entry) => (
          <ArtistCover
            artist={{ id: entry.id, name: entry.name, hasImage: entry.hasImage }}
            className="size-full"
          />
        )}
      />

      <RankedEntries
        title={t("stats.topAlbums")}
        entries={data.topAlbums}
        href={(entry) => `/albums/${entry.id}`}
        art={(entry) => (
          <AlbumCover
            album={{ id: entry.id, title: entry.name, hasCover: entry.hasImage }}
            className="size-full rounded-none"
          />
        )}
      />

      <RankedEntries
        title={t("stats.topGenres")}
        entries={data.topGenres}
        href={(entry) => `/genres?id=${entry.id}`}
        art={(entry) => (
          <Cover hasCover={false} name={entry.name} className="size-full rounded-none" />
        )}
      />
    </>
  );
}
