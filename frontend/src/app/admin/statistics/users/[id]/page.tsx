// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { Suspense, useCallback } from "react";
import { queries } from "@/lib/queries";
import { dailyPoints, DENSE_FROM, densifyDays } from "@/lib/activityScale";
import { parsePeriod, percent } from "@/lib/adminStatistics";
import { useFormat } from "@/lib/useFormat";
import { ActivityChart } from "@/components/ActivityChart";
import { ActivityHeatmap } from "@/components/ActivityHeatmap";
import { HourClock } from "@/components/HourClock";
import { PeriodTabs, useUrlFilters } from "@/components/admin/AdminFilters";
import { StatGrid } from "@/components/admin/StatGrid";
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
import { Badge } from "@/components/ui/badge";
import { Surface } from "@/components/ui/card";
import { useT } from "@/contexts/I18nContext";
import type { AdminListenerDetail, StatisticsPeriod } from "@/lib/types";

export default function AdminListenerPage() {
  const t = useT();

  return (
    <Suspense fallback={<PageHeader title={t("admin.stats.listenersTitle")} />}>
      <AdminListenerView />
    </Suspense>
  );
}

function AdminListenerView() {
  const t = useT();
  const routeParams = useParams<{ id: string }>();
  const id = routeParams.id;

  const { params, set } = useUrlFilters(`/admin/statistics/users/${id}`);
  const period = parsePeriod(params.get("period"), "Month");

  const setPeriod = useCallback(
    (next: StatisticsPeriod) => set({ period: next === "Month" ? undefined : next }),
    [set],
  );

  const detail = useQuery(queries.adminListener(id, period));

  return (
    <>
      <PageHeader
        title={detail.data?.listener.username ?? t("admin.stats.listenersTitle")}
        subtitle={detail.data?.listener.displayName}
        actions={
          <Link
            href="/admin/statistics/users"
            className="text-sm text-muted-foreground hover:text-foreground"
          >
            {t("admin.stats.backToListeners")}
          </Link>
        }
      />

      <PeriodTabs period={period} onChange={setPeriod} />

      <Query result={detail} skeleton="tile" skeletonCount={3}>
        {(data) => (
          <div className="flex flex-col gap-8">
            <Account data={data} />
            <Charts data={data} />
            <Tops data={data} />
            <RecentUploads data={data} />
          </div>
        )}
      </Query>
    </>
  );
}

function Account({ data }: { data: AdminListenerDetail }) {
  const t = useT();
  const format = useFormat();

  const { listener } = data;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        <Badge variant={listener.isAdmin ? "primary" : "outline"}>
          {listener.isAdmin ? t("admin.roleAdmin") : t("admin.roleUser")}
        </Badge>
        <Badge variant={listener.isActive ? "primary" : "neutral"}>
          {listener.isActive ? t("admin.active") : t("admin.inactive")}
        </Badge>
        <span className="text-sm text-muted-foreground">
          {t("field.created")}: {format.relativeDate(listener.createdAt)}
        </span>
        <span className="text-sm text-muted-foreground">
          {t("admin.stats.lastActive")}:{" "}
          {listener.lastActiveAt
            ? format.relativeDate(listener.lastActiveAt)
            : t("admin.stats.neverActive")}
        </span>
      </div>

      <StatGrid
        title={t("admin.stats.listening")}
        stats={[
          {
            label: t("admin.stats.listenedTime"),
            value: format.totalDuration(listener.listenedSeconds),
          },
          { label: t("admin.stats.plays"), value: String(listener.plays) },
          { label: t("admin.stats.tracksHeard"), value: String(listener.uniqueTracks) },
          { label: t("admin.stats.skipRate"), value: `${percent(listener.skipRate)}%` },
        ]}
      />

      <StatGrid
        title={t("admin.stats.uploads")}
        stats={[
          { label: t("admin.stats.uploadedTracks"), value: String(listener.uploadedTracks) },
          {
            label: t("admin.stats.uploadedBytes"),
            value: format.bytes(listener.uploadedBytes),
          },
          { label: t("admin.stats.likes"), value: String(listener.likes) },
          { label: t("admin.stats.playlists"), value: String(listener.playlists) },
        ]}
      />
    </div>
  );
}

function Charts({ data }: { data: AdminListenerDetail }) {
  const t = useT();
  const format = useFormat();

  const days = densifyDays(data.byDay, data.from);

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

      <div className="grid grid-cols-2 gap-8 max-md:grid-cols-1">
        <section className="flex flex-col gap-3">
          <SectionHeader title={t("stats.byHour")} />
          <HourClock
            hours={data.byHour}
            columnLabel={t("stats.hour")}
            tableLabel={t("stats.byHour")}
            formatValue={format.totalDuration}
          />
        </section>

        <section className="flex flex-col gap-3">
          <SectionHeader title={t("admin.stats.byPlaybackSource")} />
          <PlaybackSources data={data} />
        </section>
      </div>
    </div>
  );
}

function PlaybackSources({ data }: { data: AdminListenerDetail }) {
  const t = useT();
  const total = data.bySource.reduce((sum, entry) => sum + entry.plays, 0);

  if (total === 0) return <p className="text-sm text-muted-foreground">{t("admin.stats.empty")}</p>;

  return (
    <Surface variant="tile" padding="lg">
      <ul className="flex flex-col gap-1.5">
        {data.bySource.map((entry) => (
          <li key={entry.source} className="flex items-center gap-3 text-sm">
            <span className="w-32 shrink-0 truncate text-muted-foreground">{entry.source}</span>
            <span
              aria-hidden="true"
              className="h-1.5 min-w-0.5 rounded-full bg-primary"
              style={{ width: `${(entry.plays / total) * 100}%` }}
            />
            <span className="tabular-nums">{entry.plays}</span>
          </li>
        ))}
      </ul>
    </Surface>
  );
}

function Tops({ data }: { data: AdminListenerDetail }) {
  const t = useT();
  const format = useFormat();

  return (
    <div className="grid grid-cols-2 gap-8 max-md:grid-cols-1">
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
                href={entry.track.albumId ? `/albums/${entry.track.albumId}` : undefined}
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
    </div>
  );
}

function RecentUploads({ data }: { data: AdminListenerDetail }) {
  const t = useT();
  const format = useFormat();

  return (
    <section className="flex flex-col gap-3">
      <SectionHeader title={t("admin.stats.recentUploads")} />

      {data.recentUploads.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t("admin.stats.noUploads")}</p>
      ) : (
        <ul className="flex flex-col gap-1">
          {data.recentUploads.map((track) => (
            <li key={track.id} className="flex items-center gap-3 px-2 py-2 text-sm">
              <span className="min-w-0 flex-1 truncate">
                {track.title}
                <span className="text-muted-foreground"> · {track.artistName}</span>
              </span>
              <span className="shrink-0 tabular-nums text-muted-foreground">
                {format.bytes(track.fileSize)}
              </span>
              <span className="shrink-0 text-faint">{format.relativeDate(track.createdAt)}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
