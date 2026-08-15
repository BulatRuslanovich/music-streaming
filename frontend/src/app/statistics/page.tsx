"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { useFormat } from "@/lib/useFormat";
import { Cover } from "@/components/Cover";
import { LoadError, PageHeader, Skeleton } from "@/components/ui";
import { usePlayer } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import type { DailyActivity, HourlyActivity, StatisticsEntry, StatisticsPeriod } from "@/lib/types";

const PERIODS: StatisticsPeriod[] = ["Week", "Month", "Quarter", "Year", "All"];

export default function StatisticsPage() {
  const t = useT();
  const format = useFormat();
  const player = usePlayer();

  const [period, setPeriod] = useState<StatisticsPeriod>("Month");

  const { data, error, loading, reload } = useApi(
    () => api.statistics(period),
    [period],
    "statistics",
  );

  const empty = data !== null && data.summary.plays === 0;

  return (
    <>
      <PageHeader
        title={t("stats.title")}
        actions={
          <div className="segmented" role="tablist" aria-label={t("stats.title")}>
            {PERIODS.map((value) => (
              <button
                key={value}
                type="button"
                role="tab"
                aria-selected={value === period}
                className={`segmented-option ${value === period ? "is-active" : ""}`}
                onClick={() => setPeriod(value)}
              >
                {t(`stats.period.${value}` as const)}
              </button>
            ))}
          </div>
        }
      />

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton variant="row" count={6} />}

      {data && empty && <p className="empty-state">{t("stats.empty")}</p>}

      {data && !empty && (
        <div className="stats">
          <section className="stat-tiles">
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
          </section>

          <section className="stat-panel">
            <h2>{t("stats.byDay")}</h2>
            <DailyChart days={data.byDay} />
          </section>

          <section className="stat-panel">
            <h2>{t("stats.byHour")}</h2>
            <HourlyChart hours={data.byHour} />
          </section>

          {data.topTracks.length > 0 && (
            <section className="stat-panel">
              <h2>{t("stats.topTracks")}</h2>
              <ol className="stat-list">
                {data.topTracks.map((entry, index) => (
                  <li key={entry.track.id}>
                    <button
                      type="button"
                      className="stat-row"
                      onClick={() =>
                        player.playQueue(
                          data.topTracks.map((item) => item.track),
                          index,
                          { source: "history" },
                        )
                      }
                    >
                      <span className="stat-rank">{index + 1}</span>
                      <Cover
                        albumId={entry.track.albumId}
                        trackId={entry.track.id}
                        hasCover={entry.track.hasCover}
                        name={entry.track.albumTitle ?? entry.track.title}
                        size={40}
                      />
                      <span className="stat-meta">
                        <span className="stat-name">{entry.track.title}</span>
                        <span className="muted">{entry.track.artistName}</span>
                      </span>
                      <span className="stat-value">
                        {format.totalDuration(entry.listenedSeconds)}
                        <span className="muted">
                          {t("stats.playCount", { count: entry.plays })}
                        </span>
                      </span>
                    </button>
                  </li>
                ))}
              </ol>
            </section>
          )}

          <Ranked title={t("stats.topArtists")} entries={data.topArtists} />
          <Ranked title={t("stats.topAlbums")} entries={data.topAlbums} />
          <Ranked title={t("stats.topGenres")} entries={data.topGenres} />
        </div>
      )}
    </>
  );
}

function Tile({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <div className="stat-tile">
      <span className="stat-tile-label">{label}</span>
      <strong className="stat-tile-value">{value}</strong>
      {hint && <span className="muted">{hint}</span>}
    </div>
  );
}

function Ranked({ title, entries }: { title: string; entries: StatisticsEntry[] }) {
  const t = useT();
  const format = useFormat();

  if (entries.length === 0) return null;

  const longest = Math.max(...entries.map((entry) => entry.listenedSeconds));

  return (
    <section className="stat-panel">
      <h2>{title}</h2>
      <ol className="stat-bars">
        {entries.map((entry, index) => (
          <li key={entry.id}>
            <span className="stat-rank">{index + 1}</span>
            <span className="stat-bar-label">{entry.name}</span>
            <span
              className="stat-bar"
              style={{ ["--share" as string]: `${percent(entry.listenedSeconds, longest)}%` }}
              aria-hidden="true"
            />
            <span className="stat-value">
              {format.totalDuration(entry.listenedSeconds)}
              <span className="muted">{t("stats.playCount", { count: entry.plays })}</span>
            </span>
          </li>
        ))}
      </ol>
    </section>
  );
}

/**
 * График по дням. Столбцы рисуются разметкой и CSS, а не библиотекой: данных здесь ровно один
 * ряд, и целая зависимость ради него в бандле не окупается.
 */
function DailyChart({ days }: { days: DailyActivity[] }) {
  const format = useFormat();
  if (days.length === 0) return null;

  const longest = Math.max(...days.map((day) => day.listenedSeconds));

  return (
    <ol className="chart chart-days">
      {days.map((day) => (
        <li
          key={day.date}
          title={`${format.shortDate(day.date)} · ${format.totalDuration(day.listenedSeconds)}`}
        >
          <span
            className="chart-column"
            style={{ ["--share" as string]: `${percent(day.listenedSeconds, longest)}%` }}
          />
        </li>
      ))}
    </ol>
  );
}

/** По часам суток — всегда все двадцать четыре, чтобы форма дня читалась целиком. */
function HourlyChart({ hours: activity }: { hours: HourlyActivity[] }) {
  const format = useFormat();

  const byHour = new Map(activity.map((entry) => [entry.hour, entry.listenedSeconds]));
  const longest = Math.max(1, ...activity.map((entry) => entry.listenedSeconds));

  return (
    <ol className="chart chart-hours">
      {Array.from({ length: 24 }, (_, hour) => {
        const seconds = byHour.get(hour) ?? 0;

        return (
          <li
            key={hour}
            title={`${String(hour).padStart(2, "0")}:00 · ${format.totalDuration(seconds)}`}
          >
            <span
              className="chart-column"
              style={{ ["--share" as string]: `${percent(seconds, longest)}%` }}
            />
            {hour % 6 === 0 && <span className="chart-tick">{hour}</span>}
          </li>
        );
      })}
    </ol>
  );
}

function percent(value: number, of: number): number {
  return of <= 0 ? 0 : Math.max(2, Math.round((value / of) * 100));
}
