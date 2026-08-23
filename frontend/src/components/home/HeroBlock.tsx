// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import { cn } from "@/lib/cn";
import { formatArtists, formatDuration } from "@/lib/format";
import { trackCoverUrl } from "@/lib/media";
import { useCoverColor } from "@/lib/useCoverColor";
import type { HomeBlock, Track } from "@/lib/types";
import { usePlayer, type PlaybackOrigin } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { TrackCover } from "../Cover";
import { PauseIcon, PlayIcon } from "../Icons";
import { Button } from "../ui/button";

const PREVIEW_SIZE = 4;

export function HeroBlock({
  block,
  title,
  href,
  origin,
}: {
  block: HomeBlock;
  title: string;
  href?: string;
  origin: PlaybackOrigin;
}) {
  const t = useT();
  const player = usePlayer();

  const tracks = block.tracks ?? [];
  const selected = player.currentTrack ?? tracks[0] ?? null;
  const context = selected ? uniqueTracks([selected, ...tracks]) : tracks;
  const tint = useCoverColor(trackCoverUrl(selected, "thumb"));

  if (!selected) return null;

  const selectedIsCurrent = player.currentTrack?.id === selected.id;
  const resume = player.currentTrack !== null;

  const playSelected = () => {
    if (selectedIsCurrent) {
      player.toggle();
      return;
    }

    player.playTrack(selected, context, origin);
  };

  return (
    <section
      style={{ ["--art-tint" as string]: tint ?? "var(--primary)" }}
      className={cn(
        "grid shrink-0 grid-cols-[minmax(0,1.15fr)_minmax(17rem,0.85fr)] overflow-hidden rounded-2xl border border-border",
        "bg-[linear-gradient(125deg,color-mix(in_oklab,var(--art-tint)_16%,var(--card)),var(--card)_72%)]",
        "[transition:--art-tint_700ms_var(--ease)] max-lg:grid-cols-1",
      )}
      aria-labelledby="home-focus-heading"
    >
      <div className="flex min-w-0 items-center gap-6 p-6 max-md:items-start max-md:gap-4 max-md:p-4">
        <div className="size-44 shrink-0 overflow-hidden rounded-xl shadow-art max-md:size-28">
          <TrackCover track={selected} variant="full" className="size-full rounded-none" />
        </div>

        <div className="min-w-0">
          <p id="home-focus-heading" className="text-sm font-bold text-primary">
            {resume ? t("rec.shelf.continueListening") : t("home.dailyMixSubtitle")}
          </p>
          <h2 className="mt-2 truncate text-[clamp(1.5rem,1rem+1.4vw,2.35rem)]">
            {selected.title}
          </h2>
          <p className="mt-1 truncate text-muted-foreground">{formatArtists(selected)}</p>
          <div className="mt-5 flex flex-wrap items-center gap-3">
            <Button variant="primary" size="lg" onClick={playSelected}>
              {selectedIsCurrent && player.isPlaying ? (
                <PauseIcon size={20} />
              ) : (
                <PlayIcon size={20} />
              )}
              {selectedIsCurrent && player.isPlaying ? t("action.pause") : t("action.play")}
            </Button>
            <span className="text-sm text-faint tabular-nums">
              {formatDuration(selected.durationSeconds)}
            </span>
          </div>
        </div>
      </div>

      <div className="border-l border-border bg-black/5 p-3 max-lg:border-t max-lg:border-l-0">
        <div className="flex items-center justify-between gap-3 px-2 py-1.5">
          <p className="truncate text-xs font-bold tracking-wider text-faint uppercase">{title}</p>
          {href && (
            <Link href={href} className="text-xs font-semibold text-muted-foreground">
              {t("action.seeAll")}
            </Link>
          )}
        </div>
        <ol aria-label={title}>
          {tracks.slice(0, PREVIEW_SIZE).map((track, index) => (
            <li key={track.id}>
              <HeroTrack
                track={track}
                index={index}
                current={player.currentTrack?.id === track.id}
                playing={player.currentTrack?.id === track.id && player.isPlaying}
                onPlay={() => {
                  if (player.currentTrack?.id === track.id) {
                    player.toggle();
                    return;
                  }

                  player.playTrack(track, tracks, origin);
                }}
              />
            </li>
          ))}
        </ol>
      </div>
    </section>
  );
}

function HeroTrack({
  track,
  index,
  current,
  playing,
  onPlay,
}: {
  track: Track;
  index: number;
  current: boolean;
  playing: boolean;
  onPlay: () => void;
}) {
  const t = useT();

  return (
    <button
      type="button"
      onClick={onPlay}
      aria-label={`${playing ? t("action.pause") : t("action.play")}: ${track.title}`}
      className={cn(
        "grid w-full grid-cols-[1.25rem_2.5rem_minmax(0,1fr)_auto] items-center gap-3 rounded-lg px-2 py-2 text-left",
        current ? "bg-primary-soft" : "hover:bg-raised",
      )}
    >
      <span className="text-xs text-faint tabular-nums">{index + 1}</span>
      <span className="relative size-10 overflow-hidden rounded-md">
        <TrackCover track={track} className="size-full rounded-none" />
        {current && (
          <span
            aria-hidden="true"
            className="absolute inset-0 grid place-items-center bg-black/55 text-white"
          >
            {playing ? <PauseIcon size={15} /> : <PlayIcon size={15} />}
          </span>
        )}
      </span>
      <span className="min-w-0">
        <span className={cn("block truncate font-semibold", current && "text-primary")}>
          {track.title}
        </span>
        <span className="block truncate text-sm text-muted-foreground">{formatArtists(track)}</span>
      </span>
      <span className="text-xs text-faint tabular-nums">
        {formatDuration(track.durationSeconds)}
      </span>
    </button>
  );
}

function uniqueTracks(tracks: Track[]): Track[] {
  return [...new Map(tracks.map((track) => [track.id, track])).values()];
}
