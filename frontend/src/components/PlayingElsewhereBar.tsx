// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { MonitorSpeakerIcon, XIcon } from "lucide-react";
import { api } from "@/lib/api";
import { deviceId } from "@/lib/events";
import { formatArtists, formatDuration } from "@/lib/format";
import { readStored, writeStored } from "@/lib/storage";
import { usePlayerActions, usePlayerState } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { TrackCover } from "./Cover";
import { Button } from "./ui/button";

const REFRESH_MS = 30_000;
const DISMISSED_KEY = "music-streaming.handoff-dismissed";

export function PlayingElsewhereBar() {
  const t = useT();
  const { isPlaying } = usePlayerState();
  const { continueHere } = usePlayerActions();
  const [dismissed, setDismissed] = useState(() => readStored(DISMISSED_KEY, "session"));
  const [taking, setTaking] = useState(false);

  const { data: elsewhere } = useQuery({
    queryKey: ["playingElsewhere"],
    queryFn: async ({ signal }) => (await api.playingElsewhere(deviceId(), signal)) ?? null,
    enabled: !isPlaying,
    staleTime: 0,
    refetchInterval: REFRESH_MS,
    refetchOnWindowFocus: true,
  });

  if (isPlaying || !elsewhere) return null;

  const key = `${elsewhere.deviceId}@${elsewhere.reportedAt}`;
  if (dismissed === key) return null;

  const { track, deviceName } = elsewhere;
  const device = deviceName || t("player.anotherDevice");

  const dismiss = () => {
    writeStored(DISMISSED_KEY, key, "session");
    setDismissed(key);
  };

  const take = async () => {
    setTaking(true);
    await continueHere();
    setTaking(false);
  };

  return (
    <section
      aria-label={t("handoff.region")}
      className="flex items-center gap-3 border-t border-border bg-raised px-4 py-2 max-md:px-2.5"
    >
      <TrackCover track={track} size={36} />

      <div className="min-w-0 flex-1 leading-tight">
        <p className="flex items-center gap-1.5 text-2xs text-faint">
          <MonitorSpeakerIcon aria-hidden className="size-3.5 shrink-0" />
          <span className="truncate">
            {t(elsewhere.isPlaying ? "handoff.playingOn" : "handoff.pausedOn", { device })}
            {!elsewhere.isPlaying && ` · ${formatDuration(elsewhere.positionSeconds)}`}
          </span>
        </p>
        <p className="truncate text-sm">
          <span className="font-medium">{track.title}</span>
          <span className="text-muted-foreground"> — {formatArtists(track)}</span>
        </p>
      </div>

      <Button variant="primary" size="sm" onClick={take} disabled={taking}>
        {t("handoff.continueHere")}
      </Button>
      <Button variant="ghost" size="icon-sm" onClick={dismiss} aria-label={t("handoff.dismiss")}>
        <XIcon aria-hidden className="size-4" />
      </Button>
    </section>
  );
}
