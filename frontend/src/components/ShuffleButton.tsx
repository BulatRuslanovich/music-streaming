// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { ShuffleIcon } from "lucide-react";
import { buildOrder } from "@/lib/playback/playerQueue";
import type { Track } from "@/lib/types";
import { usePlayerActions } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { Button } from "./ui/button";

export function ShuffleButton({ tracks }: { tracks: Track[] }) {
  const t = useT();
  const player = usePlayerActions();

  const shuffle = () => {
    const order = buildOrder(tracks.length, true, -1);
    player.playQueue(
      order.map((index) => tracks[index]),
      0,
    );
  };

  return (
    <Button onClick={shuffle} disabled={tracks.length === 0}>
      <ShuffleIcon size={16} />
      {t("action.shuffle")}
    </Button>
  );
}
