// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { HomeBlock } from "@/lib/types";
import type { PlaybackOrigin } from "@/contexts/PlayerContext";
import { capFiveOnMobile } from "@/components/collection/layout";
import { RankedList } from "@/components/collection/RankedList";

export function ChartBlock({ block, origin }: { block: HomeBlock; origin: PlaybackOrigin }) {
  return <RankedList tracks={block.tracks ?? []} origin={origin} className={capFiveOnMobile} />;
}
