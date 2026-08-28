// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { formatArtists } from "@/lib/format";
import { buildOrder } from "@/lib/playerQueue";
import type { HomeBlock } from "@/lib/types";
import { usePlayback } from "@/lib/usePlayback";
import { usePlayerActions, type PlaybackOrigin } from "@/contexts/PlayerContext";
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
  const { currentTrackId, isPlaying, playTrack, playSet, setIsOnAir } = usePlayback(origin);
  const player = usePlayerActions();

  const tracks = block.tracks ?? [];
  const lead = tracks[0] ?? null;

  if (!lead) return null;

  // Блок несёт превью микса, а не весь микс, поэтому «на воздухе» проверяется по первым
  // двадцати трекам. Слушатель, ушедший дальше, увидит здесь Play вместо Pause — к этому
  // моменту он слушает третий час, и рамка «микс дня» давно описывает не то, что играет.
  const playing = setIsOnAir(tracks) && isPlaying;

  const playMix = () => playSet(tracks);

  const shuffleMix = () => {
    const order = buildOrder(tracks.length, true, -1);

    // Перемешанный порядок — это уже другая очередь, поэтому здесь не playSet: он бы
    // распознал текущий трек и поставил паузу вместо того, чтобы перемешать заново.
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
      facts={`${t("count.tracks", { count: block.totalCount ?? tracks.length })} · ${formatArtists(lead)}`}
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
      currentTrackId={currentTrackId ?? null}
      isPlaying={isPlaying}
      onPlayTrack={(track) => playTrack(track, tracks)}
    />
  );
}
