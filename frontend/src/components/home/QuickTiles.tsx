// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { HeartIcon, ListMusicIcon } from "lucide-react";
import type { Route } from "next";
import Link from "next/link";
import type { ReactNode } from "react";
import { cn } from "@/lib/cn";
import { formatArtists } from "@/lib/format";
import type { HomeBlock } from "@/lib/types";
import { usePlayback } from "@/lib/playback/usePlayback";
import { useT } from "@/contexts/I18nContext";
import { capFourOnMobile } from "@/components/collection/layout";
import { CoverMosaic } from "@/components/collection/CoverMosaic";
import { PlaylistCover, TrackCover } from "../Cover";
import { PlayBadge } from "../PlayBadge";

/** Быстрый доступ под миксом дня: избранное, недавние треки и свои плейлисты плитками. */
export function QuickTiles({ blocks }: { blocks: HomeBlock[] }) {
  return (
    <div
      className={cn(
        "grid grid-cols-[repeat(auto-fill,minmax(15rem,1fr))] gap-2 max-md:grid-cols-1",
        capFourOnMobile,
      )}
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
      {tracks.map((track) => {
        const isCurrent = currentTrackId === track.id;

        return (
          <Tile
            key={track.id}
            current={isCurrent}
            label={track.title}
            sublabel={formatArtists(track)}
            onClick={() => playTrack(track, tracks)}
            art={<TrackCover track={track} className="size-full rounded-none" />}
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
          art={
            <PlaylistCover
              playlist={playlist}
              fallback={<ListMusicIcon />}
              className="size-full rounded-none"
            />
          }
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
