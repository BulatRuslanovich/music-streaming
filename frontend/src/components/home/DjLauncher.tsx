// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { cn } from "@/lib/cn";
import type { DjMode, Track } from "@/lib/types";
import { usePlayer } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { PlayIcon } from "../Icons";
import { CoverMosaic } from "./CoverMosaic";
import { Tile } from "./Tile";

const MODES: DjMode[] = ["ForYou", "Rediscover", "Discover", "Flow"];

export function DjLauncher({ tracks }: { tracks: Track[] }) {
  const t = useT();
  const player = usePlayer();

  return (
    <>
      {MODES.map((mode, index) => {
        const active = player.dj?.mode === mode;
        const sublabel =
          mode === "Flow" && player.currentTrack
            ? player.currentTrack.title
            : t(`dj.mode.${mode}.hint`);

        return (
          <Tile
            key={mode}
            current={active}
            pressed={active}
            disabled={player.djLoading}
            label={t(`dj.mode.${mode}`)}
            sublabel={sublabel}
            onClick={() => void player.startDj(mode, mode === "Flow" ? player.currentTrack : null)}
            art={<CoverMosaic tracks={artworkFor(tracks, index)} />}
            action={
              <span
                aria-hidden="true"
                className={cn(
                  "grid size-8 shrink-0 place-items-center rounded-full bg-primary text-primary-foreground shadow-art",
                  "transition-transform duration-150 ease-brand group-active:scale-95",
                )}
              >
                <PlayIcon size={16} />
              </span>
            }
          />
        );
      })}
    </>
  );
}

function artworkFor(tracks: Track[], modeIndex: number): Track[] {
  if (tracks.length <= 4) return tracks;

  return Array.from({ length: 4 }, (_, offset) => tracks[(modeIndex * 3 + offset) % tracks.length]);
}
