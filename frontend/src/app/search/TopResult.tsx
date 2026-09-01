// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import { cn } from "@/lib/cn";
import { formatArtists } from "@/lib/format";
import type { SearchTopResult } from "@/lib/types";
import { usePlayback } from "@/lib/usePlayback";
import { useT } from "@/contexts/I18nContext";
import { AlbumMosaic } from "@/components/collection/CoverMosaic";
import { Section } from "@/components/collection/Section";
import { heroSurface } from "@/components/collection/Spotlight";
import { AlbumCover, ArtistCover, TrackCover } from "@/components/Cover";
import { PauseIcon, PlayIcon } from "@/components/Icons";
import { Button } from "@/components/ui/button";
import { Overline } from "@/components/ui/label";

export function TopResult({ top }: { top: SearchTopResult }) {
  const t = useT();

  return (
    <Section title={t("search.topResult")}>
      {top.kind === "Track" && top.track ? (
        <TrackTop track={top.track} />
      ) : top.kind === "Album" && top.album ? (
        <Card
          href={`/albums/${top.album.id}`}
          kind={t("albums.kind")}
          title={top.album.title}
          subtitle={top.album.artistName}
          art={<AlbumCover album={top.album} className="size-full rounded-none" />}
        />
      ) : top.kind === "Artist" && top.artist ? (
        <Card
          href={`/artists/${top.artist.id}`}
          kind={t("artists.kind")}
          title={top.artist.name}
          subtitle={t("count.tracks", { count: top.artist.trackCount })}
          round
          art={<ArtistCover artist={top.artist} className="size-full" />}
        />
      ) : top.genre ? (
        <Card
          href={`/genres?id=${top.genre.id}`}
          kind={t("field.genre")}
          title={top.genre.name}
          subtitle={t("count.tracks", { count: top.genre.trackCount })}
          art={<AlbumMosaic albumIds={top.genre.coverAlbumIds} name={top.genre.name} />}
        />
      ) : null}
    </Section>
  );
}

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className={cn(heroSurface, "flex items-center gap-5 p-5 max-md:gap-4 max-md:p-4")}>
      {children}
    </div>
  );
}

function Card({
  href,
  kind,
  title,
  subtitle,
  art,
  round = false,
}: {
  href: string;
  kind: string;
  title: string;
  subtitle: React.ReactNode;
  art: React.ReactNode;
  round?: boolean;
}) {
  return (
    <Shell>
      <span
        className={cn(
          "size-28 shrink-0 overflow-hidden rounded-lg shadow-art max-md:size-20",
          round && "rounded-full",
        )}
      >
        {art}
      </span>

      <span className="flex min-w-0 flex-col gap-1">
        <Overline>{kind}</Overline>
        <Link href={href} className="truncate text-title font-semibold hover:no-underline">
          {title}
        </Link>
        <span className="truncate text-muted-foreground">{subtitle}</span>
      </span>
    </Shell>
  );
}

function TrackTop({ track }: { track: NonNullable<SearchTopResult["track"]> }) {
  const t = useT();
  const { playTrack, soundingNow } = usePlayback({ source: "search" });

  const playing = soundingNow(track.id);

  return (
    <Shell>
      <span className="size-28 shrink-0 overflow-hidden rounded-lg shadow-art max-md:size-20">
        <TrackCover track={track} variant="full" className="size-full rounded-none" />
      </span>

      <span className="flex min-w-0 flex-col gap-1">
        <Overline>{t("nav.tracks")}</Overline>
        <span className="truncate text-title font-semibold">{track.title}</span>
        <span className="truncate text-muted-foreground">{formatArtists(track)}</span>
        <span className="mt-2">
          <Button variant="primary" onClick={() => playTrack(track, [track])}>
            {playing ? <PauseIcon size={16} /> : <PlayIcon size={16} />}
            {playing ? t("action.pause") : t("action.play")}
          </Button>
        </span>
      </span>
    </Shell>
  );
}
