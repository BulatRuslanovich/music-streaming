// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { formatArtists } from "@/lib/format";
import { trackCoverUrl } from "@/lib/media";
import { buildOrder } from "@/lib/playerQueue";
import { useCoverColor } from "@/lib/useCoverColor";
import type { HomeBlock } from "@/lib/types";
import { usePlayer, type PlaybackOrigin } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { Spotlight } from "@/components/collection/Spotlight";
import { TrackCover } from "../Cover";
import { PauseIcon, PlayIcon, ShuffleIcon } from "../Icons";
import { Button } from "../ui/button";

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
  const lead = tracks[0] ?? null;

  const tint = useCoverColor(trackCoverUrl(lead, "thumb"));

  if (!lead) return null;

  const onAir =
    player.currentTrack !== null && tracks.some((track) => track.id === player.currentTrack?.id);
  const playing = onAir && player.isPlaying;

  const playMix = () => {
    if (onAir) {
      player.toggle();
      return;
    }

    player.playQueue(tracks, 0, origin);
  };

  const shuffleMix = () => {
    const order = buildOrder(tracks.length, true, -1);

    player.playQueue(
      order.map((index) => tracks[index]),
      0,
      origin,
    );
  };

  return (
    <Spotlight
      headingId="home-focus-heading"
      eyebrow={t("home.dailyMixSubtitle")}
      title={title}
      tint={tint}
      facts={`${t("count.tracks", { count: tracks.length })} · ${formatArtists(lead)}`}
      art={<TrackCover track={lead} variant="full" className="size-full rounded-none" />}
      actions={
        <>
          <Button variant="primary" size="lg" onClick={playMix}>
            {playing ? <PauseIcon size={20} /> : <PlayIcon size={20} />}
            {playing ? t("action.pause") : t("action.play")}
          </Button>
          <Button variant="secondary" size="lg" onClick={shuffleMix}>
            <ShuffleIcon size={18} />
            {t("action.shuffle")}
          </Button>
        </>
      }
      tracks={tracks}
      href={href}
      currentTrackId={player.currentTrack?.id ?? null}
      isPlaying={player.isPlaying}
      onPlayTrack={(track) => {
        if (player.currentTrack?.id === track.id) {
          player.toggle();
          return;
        }

        player.playTrack(track, tracks, origin);
      }}
    />
  );
}
