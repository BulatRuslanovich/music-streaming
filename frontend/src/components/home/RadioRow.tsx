// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { cn } from "@/lib/cn";
import type { DjMode, Track } from "@/lib/types";
import { usePlayer } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { PlayBadge } from "../PlayBadge";
import { CoverMosaic } from "./CoverMosaic";

const MODES: DjMode[] = ["ForYou", "Rediscover", "Discover", "Flow"];

/**
 * Радиостанции стоят рядом с миксом дня и намеренно не похожи на плитки треков:
 * это генеративная бесконечная очередь, а не ярлык на конкретную вещь.
 */
export function RadioRow({ tracks }: { tracks: Track[] }) {
  const t = useT();
  const player = usePlayer();

  return (
    <div className="grid grid-cols-4 gap-3 max-md:grid-cols-2 max-md:gap-2 [&>*]:animate-rise">
      {MODES.map((mode, index) => {
        const active = player.dj?.mode === mode;
        const sublabel =
          mode === "Flow" && player.currentTrack
            ? player.currentTrack.title
            : t(`dj.mode.${mode}.hint`);

        return (
          <button
            key={mode}
            type="button"
            aria-pressed={active}
            disabled={player.djLoading}
            onClick={() => void player.startDj(mode, mode === "Flow" ? player.currentTrack : null)}
            className={cn(
              "group relative flex h-24 flex-col justify-end overflow-hidden rounded-xl p-3 text-left",
              "shadow-panel transition-transform duration-150 ease-brand active:scale-[0.99]",
              "disabled:pointer-events-none disabled:opacity-55 max-md:h-20",
              active && "inset-ring-2 inset-ring-primary",
            )}
          >
            <span aria-hidden="true" className="absolute inset-0 block opacity-40">
              <CoverMosaic tracks={artworkFor(tracks, index)} />
            </span>
            <span
              aria-hidden="true"
              className="absolute inset-0 bg-[linear-gradient(155deg,color-mix(in_oklab,var(--primary)_30%,transparent),rgb(0_0_0/78%))]"
            />

            <span className="relative flex items-end justify-between gap-2">
              <span className="min-w-0">
                <span className="block truncate font-semibold text-white">
                  {t(`dj.mode.${mode}`)}
                </span>
                <span className="block truncate text-xs text-white/70">{sublabel}</span>
              </span>
              <PlayBadge size={8} playing={false} visible={active} />
            </span>
          </button>
        );
      })}
    </div>
  );
}

function artworkFor(tracks: Track[], modeIndex: number): Track[] {
  if (tracks.length <= 4) return tracks;

  return Array.from({ length: 4 }, (_, offset) => tracks[(modeIndex * 3 + offset) % tracks.length]);
}
