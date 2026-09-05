// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { CoverMosaic } from "@/components/collection/CoverMosaic";
import { Delta } from "@/components/collection/Delta";
import {
  Ranked,
  RankedEntries,
  RankedValue,
  rankedShare,
} from "@/components/collection/RankedEntries";
import { RankedRow } from "@/components/collection/RankedRow";
import { ArtistCover, TrackCover } from "@/components/Cover";
import { DetailHero } from "@/components/DetailHero";
import { DownloadIcon, PlayIcon, PlaylistIcon, SparkleIcon } from "@/components/Icons";
import { Query } from "@/components/Query";
import { RecapStory } from "@/components/recap/RecapStory";
import { Button } from "@/components/ui/button";
import { Surface } from "@/components/ui/card";
import { Overline } from "@/components/ui/label";
import { useI18n, useT } from "@/contexts/I18nContext";
import { usePlayerActions } from "@/contexts/PlayerContext";
import { useToast } from "@/contexts/ToastContext";
import { api } from "@/lib/api";
import { ApiError } from "@/lib/http";
import { trackCoverUrl } from "@/lib/media";
import { queries } from "@/lib/queries";
import { downloadRecapCard, listeningChange, monthLabel, type MonthlyRecap } from "@/lib/recap";
import { useCoverColor } from "@/lib/useCoverColor";
import { useFormat } from "@/lib/useFormat";
import { useInvalidate } from "@/lib/useInvalidate";
import { useRecapWindow } from "@/lib/useRecapWindow";

/** Итоги собираются из истории прослушивания, поэтому очередь отсюда — тот же источник. */
const ORIGIN = { source: "history" } as const;

const STORY_SEEN_KEY = "caimack.recapStorySeen";

/** Запоминается месяц, а не флаг: следующие итоги должны развернуться сами. */
function storySeen(month: string): boolean {
  try {
    return localStorage.getItem(STORY_SEEN_KEY) === month;
  } catch {
    return false;
  }
}

function rememberStory(month: string): void {
  try {
    localStorage.setItem(STORY_SEEN_KEY, month);
  } catch {
    // Приватное окно — история просто развернётся ещё раз в следующий заход.
  }
}

export default function RecapPage() {
  const t = useT();
  const router = useRouter();
  const period = useRecapWindow();
  const result = useQuery({ ...queries.monthlyRecap(), enabled: period?.open === true });

  const closed = period !== null && !period.open;
  // 404 значит, что сервер уже перевёл сутки, а вкладка открыта со вчера: ведём себя так же,
  // как при закрытом окне, вместо экрана ошибки про несуществующую страницу.
  const gone = result.error instanceof ApiError && result.error.status === 404;

  useEffect(() => {
    if (closed || gone) router.replace("/");
  }, [closed, gone, router]);

  if (period === null || closed || gone) return null;

  return (
    <Query
      result={result}
      skeleton="detail"
      isEmpty={(data) => data.listenedSeconds === 0}
      empty={{ title: t("recap.empty"), description: t("recap.emptyHint") }}
    >
      {(data) => <Recap data={data} />}
    </Query>
  );
}

function Recap({ data }: { data: MonthlyRecap }) {
  const { t, locale } = useI18n();
  const format = useFormat();
  const player = usePlayerActions();
  const router = useRouter();
  const invalidate = useInvalidate();
  const { notify } = useToast();
  const [busy, setBusy] = useState(false);

  // История разворачивается сама один раз за месяц — дальше остаётся кнопка. Подарок
  // открывают однажды, но пересмотреть его никто не мешает.
  const [story, setStory] = useState(() => !storySeen(data.month));

  const tracks = data.topTracks.map((entry) => entry.track);
  const title = monthLabel(data.month, locale);
  const change = listeningChange(data.listenedSeconds, data.previousListenedSeconds);

  // Цвет шапки приходит из настоящей обложки месяца — того же источника, что и мозаика.
  const tint = useCoverColor(trackCoverUrl(tracks[0], "thumb"));

  const cardLines = [
    format.totalDuration(data.listenedSeconds),
    t("count.tracks", { count: data.uniqueTracks }),
    t("count.artists", { count: data.uniqueArtists }),
    ...(data.topArtists[0] ? [t("recap.artistNamed", { name: data.topArtists[0].name })] : []),
    ...(data.topGenre ? [t("recap.genreNamed", { name: data.topGenre })] : []),
  ];

  const savePlaylist = async () => {
    setBusy(true);
    try {
      const playlist = await api.saveRecapPlaylist(t("recap.playlistName", { month: title }));
      invalidate("playlists");
      router.push(`/playlists/${playlist.id}`);
    } catch {
      notify(t("recap.failed"), "error");
    } finally {
      setBusy(false);
    }
  };

  const saveImage = () =>
    void downloadRecapCard({
      eyebrow: t("recap.title"),
      title,
      lines: cardLines,
      covers: tracks.slice(0, 4).map((track) => trackCoverUrl(track, "full")),
      filename: `caimack-${data.month}.png`,
    }).catch(() => notify(t("recap.failed"), "error"));

  const closeStory = () => {
    setStory(false);
    rememberStory(data.month);
  };

  return (
    <>
      {story && <RecapStory data={data} onClose={closeStory} />}

      <DetailHero
        kind={t("recap.title")}
        title={title}
        tint={tint}
        description={t("recap.subtitle")}
        art={<CoverMosaic tracks={tracks} />}
        facts={`${format.totalDuration(data.listenedSeconds)} · ${t("count.tracks", {
          count: data.uniqueTracks,
        })} · ${t("count.artists", { count: data.uniqueArtists })}`}
        actions={
          <>
            <Button variant="primary" onClick={() => setStory(true)}>
              <SparkleIcon size={18} /> {t("recap.watchStory")}
            </Button>
            <Button variant="secondary" onClick={() => player.playQueue(tracks, 0, ORIGIN)}>
              <PlayIcon size={18} /> {t("recap.listen")}
            </Button>
            <Button variant="secondary" disabled={busy} onClick={() => void savePlaylist()}>
              <PlaylistIcon size={16} /> {t("recap.savePlaylist")}
            </Button>
            <Button variant="secondary" onClick={saveImage}>
              <DownloadIcon size={16} /> {t("recap.saveImage")}
            </Button>
          </>
        }
      />

      <Summary data={data} change={change} />

      {data.topTracks.length > 0 && (
        <Ranked title={t("recap.topTracks")}>
          {data.topTracks.map((entry, index) => (
            <li key={entry.track.id}>
              <RankedRow
                rank={index + 1}
                featured={index === 0}
                title={entry.track.title}
                subtitle={entry.track.artistName}
                bar={rankedShare(entry.listenedSeconds, data.topTracks[0].listenedSeconds)}
                art={<TrackCover track={entry.track} className="size-full rounded-none" />}
                onClick={() => player.playQueue(tracks, index, ORIGIN)}
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
        title={t("recap.topArtists")}
        entries={data.topArtists}
        href={(entry) => `/artists/${entry.id}`}
        art={(entry) => (
          <ArtistCover
            artist={{ id: entry.id, name: entry.name, hasImage: entry.hasImage }}
            className="size-full"
          />
        )}
      />

      {data.discoveries.length > 0 && (
        <div className="flex flex-col gap-2">
          <RankedEntries
            title={t("recap.discoveries")}
            entries={data.discoveries}
            href={(entry) => `/artists/${entry.id}`}
            art={(entry) => (
              <ArtistCover
                artist={{ id: entry.id, name: entry.name, hasImage: entry.hasImage }}
                className="size-full"
              />
            )}
          />
          <p className="text-2xs text-faint">{t("recap.discoveryHint")}</p>
        </div>
      )}
    </>
  );
}

function Summary({ data, change }: { data: MonthlyRecap; change: number | null }) {
  const t = useT();
  const format = useFormat();

  const facts = [
    { label: t("recap.statTracks"), value: data.uniqueTracks.toLocaleString() },
    { label: t("recap.statArtists"), value: data.uniqueArtists.toLocaleString() },
    { label: t("recap.statGenre"), value: data.topGenre ?? "—" },
    { label: t("recap.statDiscoveries"), value: data.discoveries.length.toLocaleString() },
  ];

  const { topGenre, previousTopGenre } = data;
  const genreShifted = Boolean(topGenre && previousTopGenre && topGenre !== previousTopGenre);

  return (
    <Surface variant="tile" padding="lg" className="flex flex-col gap-5">
      <div className="flex flex-wrap items-end justify-between gap-x-6 gap-y-3">
        <div className="min-w-0">
          <Overline>{t("stats.listeningTime")}</Overline>
          <p className="mt-1 text-display font-bold tabular-nums">
            {format.totalDuration(data.listenedSeconds)}
          </p>
          <p className="mt-2 text-sm text-muted-foreground">
            {t("stats.playCount", { count: data.plays })}
          </p>
        </div>

        {change !== null && (
          <Delta
            percent={change}
            previousSeconds={data.previousListenedSeconds}
            caption={t("recap.versusPrevious")}
          />
        )}
      </div>

      <dl className="grid grid-cols-4 gap-4 border-t border-border pt-4 max-md:grid-cols-2">
        {facts.map((fact) => (
          <div key={fact.label} className="flex min-w-0 flex-col gap-0.5">
            <dt className="text-2xs font-bold tracking-[0.08em] text-faint uppercase">
              {fact.label}
            </dt>
            <dd className="truncate text-xl font-semibold tabular-nums">{fact.value}</dd>
          </div>
        ))}
      </dl>

      {topGenre && previousTopGenre && genreShifted && (
        <p className="text-sm text-muted-foreground">
          {t("recap.genreShift", { from: previousTopGenre, to: topGenre })}
        </p>
      )}
    </Surface>
  );
}
