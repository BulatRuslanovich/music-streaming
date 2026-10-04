// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { CheckIcon, GaugeIcon } from "lucide-react";
import { useEffect, useState } from "react";
import { cn } from "@/lib/cn";
import { formatAudioSpec } from "@/lib/format";
import { STREAM_CHANGE } from "@/lib/playback/adaptivePlayback";
import type { AudioQuality, Track } from "@/lib/types";
import { useSettings } from "@/lib/useSettings";
import { useT } from "@/contexts/I18nContext";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "./ui/dropdown-menu";

const QUALITIES: AudioQuality[] = ["Low", "Normal", "Original"];

const AUDIO_EVENTS = [STREAM_CHANGE, "loadedmetadata", "playing"];

export function StreamQuality({ track, className }: { track: Track; className?: string }) {
  const t = useT();
  const settings = useSettings();
  const [stream, setStream] = useState<string | null>(null);

  useEffect(() => {
    const read = () => {
      const audio = [...document.querySelectorAll<HTMLAudioElement>("audio[data-track-id]")].find(
        (element) => element.dataset.trackId === track.id,
      );

      if (!audio || audio.dataset.sourceLoading === "true") setStream(null);
      else if (audio.dataset.playbackMode === "hls.js") {
        setStream(audio.dataset.streamKbps ? `${audio.dataset.streamKbps}k` : null);
      } else setStream(formatAudioSpec(track) ?? t("settings.quality.Original"));
    };

    read();
    for (const name of AUDIO_EVENTS) document.addEventListener(name, read, true);
    return () => {
      for (const name of AUDIO_EVENTS) document.removeEventListener(name, read, true);
    };
  }, [track, t]);

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        aria-label={t("player.quality", { stream: stream ?? "…" })}
        className={cn(
          "inline-flex h-7 shrink-0 items-center gap-1 rounded-full border border-border-strong px-2.5 text-2xs font-semibold whitespace-nowrap text-muted-foreground tabular-nums transition-colors hover:border-foreground hover:text-foreground",
          settings.dataSaver && "border-primary text-primary",
          className,
        )}
      >
        {settings.dataSaver && <GaugeIcon size={12} />}
        {stream ?? "…"}
      </DropdownMenuTrigger>

      <DropdownMenuContent>
        <DropdownMenuLabel>{t("settings.quality")}</DropdownMenuLabel>
        {QUALITIES.map((quality) => (
          <DropdownMenuItem key={quality} onSelect={() => settings.update({ quality })}>
            <CheckIcon className={cn(settings.quality !== quality && "invisible")} />
            {t(`settings.quality.${quality}`)}
          </DropdownMenuItem>
        ))}
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={() => settings.update({ dataSaver: !settings.dataSaver })}>
          <CheckIcon className={cn(!settings.dataSaver && "invisible")} />
          {t("player.dataSaver")}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
