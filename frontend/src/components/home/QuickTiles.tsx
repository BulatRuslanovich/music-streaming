// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { HeartIcon, ListMusicIcon } from "lucide-react";
import type { Route } from "next";
import Link from "next/link";
import type { CSSProperties, ReactNode } from "react";
import { cn } from "@/lib/cn";
import { formatArtists } from "@/lib/format";
import type { HomeBlock } from "@/lib/types";
import { usePlayback } from "@/lib/playback/usePlayback";
import { useT } from "@/contexts/I18nContext";
import { capFourOnMobile } from "@/components/home/layout";
import { CoverMosaic } from "@/components/collection/CoverMosaic";
import { AlbumCover, PlaylistCover, TrackCover } from "../Cover";
import { PlayBadge } from "../PlayBadge";

export function QuickTiles({ blocks }: { blocks: HomeBlock[] }) {
  const count = blocks.reduce(
    (sum, block) =>
      sum +
      (block.layout === "Tile"
        ? 1
        : (block.albums?.length ?? 0) +
          (block.tracks?.length ?? 0) +
          (block.playlists?.length ?? 0)),
    0,
  );
  // Rows are balanced (6 tiles → 3 + 3, not 4 + 2) and a shorter last row stretches to the edge.
  const perRow = Math.ceil(count / Math.ceil(count / 4));

  return (
    <div
      className={cn(
        "flex flex-wrap gap-2 max-md:flex-col",
        "[&>*]:grow [&>*]:basis-[max(15rem,calc((100%-(var(--per-row)-1)*0.5rem)/var(--per-row)))]",
        capFourOnMobile,
      )}
      style={{ "--per-row": perRow } as CSSProperties}
    >
      {blocks.map((block) =>
        block.layout === "Tile" ? (
          <FavoritesTile key={block.key} block={block} />
        ) : (
          <RecentTiles key={block.key} block={block} />
        ),
      )}
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

function RecentTiles({ block }: { block: HomeBlock }) {
  const t = useT();
  const { currentTrackId, playTrack, soundingNow } = usePlayback();

  const tracks = block.tracks ?? [];

  return (
    <>
      {(block.albums ?? []).map((album) => (
        <Tile
          key={album.id}
          href={`/albums/${album.id}`}
          label={album.title}
          sublabel={album.artistName}
          art={<AlbumCover album={album} />}
        />
      ))}

      {tracks.map((track) => {
        const isCurrent = currentTrackId === track.id;

        return (
          <Tile
            key={track.id}
            current={isCurrent}
            label={track.title}
            sublabel={formatArtists(track)}
            onClick={() => playTrack(track, tracks)}
            art={<TrackCover track={track} />}
            action={<PlayBadge size={8} playing={soundingNow(track.id)} visible={isCurrent} />}
          />
        );
      })}

      {(block.playlists ?? []).map((playlist) => (
        <Tile
          key={playlist.id}
          href={`/playlists/${playlist.id}`}
          label={playlist.name}
          sublabel={t("count.tracks", { count: playlist.trackCount })}
          art={<PlaylistCover playlist={playlist} fallback={<ListMusicIcon />} />}
        />
      ))}
    </>
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
