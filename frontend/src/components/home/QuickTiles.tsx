// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { HeartIcon, ListMusicIcon } from "lucide-react";
import type { Route } from "next";
import Link from "next/link";
import type { CSSProperties, ReactNode } from "react";
import { cn } from "@/lib/cn";
import { PlaySource } from "@/lib/playback/playSource";
import { useShelfImpression } from "@/lib/useShelfImpression";
import { formatArtists } from "@/lib/format";
import type { HomeBlock, Track } from "@/lib/types";
import { usePlayback } from "@/lib/playback/usePlayback";
import { useT } from "@/contexts/I18nContext";
import { capFourOnMobile } from "@/components/home/layout";
import { CoverMosaic } from "@/components/collection/CoverMosaic";
import { AlbumCover, PlaylistCover, TrackCover } from "../Cover";
import { PlayBadge } from "../PlayBadge";

// Плитки идут только полными рядами одной ширины: 5 плиток — это ряд из 4, а не 3 + 2
// с растянутым хвостом. Лишняя плитка — самая старая из недавних, её не жалко.
export function tileLayout(count: number): { perRow: number; visible: number } {
  if (count <= 4) return { perRow: Math.max(count, 1), visible: count };

  const best = [4, 3]
    .map((perRow) => ({ perRow, visible: Math.min(2, Math.floor(count / perRow)) * perRow }))
    .sort((a, b) => b.visible - a.visible || b.perRow - a.perRow)[0];

  return best;
}

export function QuickTiles({ blocks }: { blocks: HomeBlock[] }) {
  const ref = useShelfImpression<HTMLDivElement>("home:quickTiles");

  const tiles = blocks.flatMap((block) =>
    block.layout === "Tile"
      ? [<FavoritesTile key={block.key} block={block} />]
      : recentTiles(block).map((tile) => (
          <PlaySource key={tile.key} value={`home:${block.baseKey}`}>
            {tile}
          </PlaySource>
        )),
  );

  const { perRow, visible } = tileLayout(tiles.length);

  return (
    <div
      ref={ref}
      className={cn(
        "grid gap-2 max-md:grid-cols-1",
        "grid-cols-[repeat(var(--per-row),minmax(0,1fr))] max-lg:grid-cols-2",
        capFourOnMobile,
      )}
      style={{ "--per-row": perRow } as CSSProperties}
    >
      {tiles.slice(0, visible)}
    </div>
  );
}

function FavoritesTile({ block }: { block: HomeBlock }) {
  const t = useT();
  const tracks = block.tracks ?? [];

  return (
    <Tile
      href="/favorites"
      label={t("home.likedSongs")}
      sublabel={t("count.tracks", { count: block.totalCount ?? tracks.length })}
      art={<CoverMosaic tracks={tracks} />}
      action={<HeartIcon size={16} className="shrink-0 fill-current text-primary" />}
    />
  );
}

function recentTiles(block: HomeBlock) {
  const tracks = block.tracks ?? [];

  return [
    ...(block.albums ?? []).map((album) => <AlbumTile key={album.id} album={album} />),
    ...tracks.map((track) => <TrackTile key={track.id} track={track} context={tracks} />),
    ...(block.playlists ?? []).map((playlist) => (
      <PlaylistTile key={playlist.id} playlist={playlist} />
    )),
  ];
}

function AlbumTile({ album }: { album: NonNullable<HomeBlock["albums"]>[number] }) {
  return (
    <Tile
      href={`/albums/${album.id}`}
      label={album.title}
      sublabel={album.artistName}
      art={<AlbumCover album={album} />}
    />
  );
}

function TrackTile({ track, context }: { track: Track; context: Track[] }) {
  const { currentTrackId, playTrack, soundingNow } = usePlayback();
  const isCurrent = currentTrackId === track.id;

  return (
    <Tile
      current={isCurrent}
      label={track.title}
      sublabel={formatArtists(track)}
      onClick={() => playTrack(track, context)}
      art={<TrackCover track={track} />}
      action={<PlayBadge size={8} playing={soundingNow(track.id)} visible={isCurrent} />}
    />
  );
}

function PlaylistTile({ playlist }: { playlist: NonNullable<HomeBlock["playlists"]>[number] }) {
  const t = useT();

  return (
    <Tile
      href={`/playlists/${playlist.id}`}
      label={playlist.name}
      sublabel={t("count.tracks", { count: playlist.trackCount })}
      art={<PlaylistCover playlist={playlist} fallback={<ListMusicIcon />} />}
    />
  );
}

function Tile<T extends string>({
  href,
  onClick,
  art,
  label,
  sublabel,
  current = false,
  action,
}: {
  href?: Route<T>;
  onClick?: () => void;
  art: ReactNode;
  label: string;
  sublabel?: ReactNode;
  current?: boolean;
  action?: ReactNode;
}) {
  const body = (
    <>
      <span className="size-14 shrink-0 overflow-hidden">{art}</span>
      <span className="flex min-w-0 flex-1 flex-col">
        <span className={cn("truncate text-sm font-medium", current && "text-primary")}>
          {label}
        </span>
        {sublabel && <span className="truncate text-xs text-muted-foreground">{sublabel}</span>}
      </span>
      {action}
    </>
  );

  const shell =
    "group flex h-14 items-center gap-3 overflow-hidden rounded-sm bg-card pr-3 text-left transition-colors duration-150 ease-brand hover:bg-raised hover:no-underline";

  return href ? (
    <Link href={href} className={shell}>
      {body}
    </Link>
  ) : (
    <button type="button" onClick={onClick} className={shell}>
      {body}
    </button>
  );
}
