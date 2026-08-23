// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { HomeBlock } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { HeartIcon } from "../Icons";
import { CoverMosaic } from "@/components/collection/CoverMosaic";
import { Tile } from "@/components/collection/Tile";

export function FavoritesTile({ block }: { block: HomeBlock }) {
  const t = useT();

  const tracks = block.tracks ?? [];
  const total = block.totalCount ?? tracks.length;

  return (
    <Tile
      href="/favorites"
      label={t("home.likedSongs")}
      sublabel={t("count.tracks", { count: total })}
      className="bg-[linear-gradient(120deg,var(--primary-soft),var(--card)_70%)]"
      art={<CoverMosaic tracks={tracks} />}
      action={
        <span className="grid size-8 shrink-0 place-items-center rounded-full bg-primary-soft text-primary">
          <HeartIcon size={16} filled />
        </span>
      }
    />
  );
}
