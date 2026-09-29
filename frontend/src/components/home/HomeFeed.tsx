// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { PauseIcon, PlayIcon, ShuffleIcon } from "lucide-react";
import type { Route } from "next";
import { cn } from "@/lib/cn";
import { formatArtists } from "@/lib/format";
import { buildOrder } from "@/lib/playback/playerQueue";
import { usePlayback } from "@/lib/playback/usePlayback";
import type { HomeBlock, Track } from "@/lib/types";
import { usePlayerActions } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { capFiveOnMobile, capFourOnMobile, deferredSection } from "@/components/collection/layout";
import { RankedList } from "@/components/collection/RankedList";
import { Spotlight } from "@/components/collection/Spotlight";
import { TrackCover } from "../Cover";
import { AlbumCard, ArtistCard, PlaylistCard, TrackCards } from "../MediaCard";
import { CardGrid, Section } from "../PageHeader";
import { Shelf } from "../Shelf";
import { Button } from "../ui/button";
import { blockHref, blockNote, blockTitle } from "./blockMeta";
import { QuickTiles } from "./QuickTiles";

export function HomeFeed({ blocks }: { blocks: HomeBlock[] }) {
  const quick = blocks.filter((block) => block.zone === "Quick");

  return (
    <>
      {blocks
        .filter((block) => block.zone === "Lead")
        .map((block) => (
          <Block key={block.key} block={block} />
        ))}

      {quick.length > 0 && <QuickTiles blocks={quick} />}

      {blocks
        .filter((block) => block.zone === "Browse")
        .map((block) => (
          <Block key={block.key} block={block} />
        ))}
    </>
  );
}

function Block({ block }: { block: HomeBlock }) {
  const t = useT();

  const title = blockTitle(block, t);
  const note = blockNote(block, t);
  const href = blockHref(block);

  // Зона Browse целиком под сгибом, поэтому её секции считаются только при подъезде к экрану.
  const section = cn(block.zone === "Browse" && deferredSection);

  switch (block.layout) {
    case "Hero":
      return <DailyMix block={block} title={title} href={href} />;

    case "Grid": {
      const tracks = block.tracks ?? [];

      return (
        <Section title={title} note={note} href={href} className={section}>
          <CardGrid className={capFourOnMobile}>
            <TrackCards tracks={onePerAlbum(tracks)} context={tracks} />
          </CardGrid>
        </Section>
      );
    }

    case "Chart":
      return (
        <Section title={title} note={note} href={href} className={section}>
          <RankedList tracks={block.tracks ?? []} className={capFiveOnMobile} />
        </Section>
      );

    default:
      return (
        <Shelf title={title} note={note} href={href} className={section}>
          <ShelfItems block={block} />
        </Shelf>
      );
  }
}

function ShelfItems({ block }: { block: HomeBlock }) {
  if (block.artists?.length) {
    return block.artists.map((artist) => <ArtistCard key={artist.id} artist={artist} bare />);
  }

  if (block.albums?.length) {
    return block.albums.map((album) => <AlbumCard key={album.id} album={album} />);
  }

  if (block.playlists?.length) {
    return block.playlists.map((playlist) => (
      <PlaylistCard key={playlist.id} playlist={playlist} />
    ));
  }

  const tracks = block.tracks ?? [];

  return <TrackCards tracks={tracks} context={tracks} />;
}

/**
 * Микс дня — якорь главной. Блок несёт превью микса, а не весь микс, поэтому «на воздухе»
 * проверяется по нему: слушатель, ушедший дальше, увидит Play вместо Pause — к этому моменту
 * рамка «микс дня» уже описывает не то, что играет.
 */
function DailyMix<T extends string>({
  block,
  title,
  href,
}: {
  block: HomeBlock;
  title: string;
  href?: Route<T>;
}) {
  const t = useT();
  const { currentTrackId, isPlaying, playTrack, playSet, setIsOnAir } = usePlayback();
  const player = usePlayerActions();

  const tracks = block.tracks ?? [];
  const lead = tracks[0];

  if (!lead) return null;

  const playing = setIsOnAir(tracks) && isPlaying;

  // Перемешанный порядок — это уже другая очередь, поэтому здесь не playSet: он бы
  // распознал текущий трек и поставил паузу вместо того, чтобы перемешать заново.
  const shuffle = () => {
    const order = buildOrder(tracks.length, true, -1);
    player.playQueue(
      order.map((index) => tracks[index]),
      0,
    );
  };

  return (
    <Spotlight
      headingId="home-focus-heading"
      note={t("home.dailyMixSubtitle")}
      title={title}
      facts={`${t("count.tracks", { count: block.totalCount ?? tracks.length })} · ${formatArtists(lead)}`}
      art={<TrackCover track={lead} variant="full" className="size-full rounded-none" />}
      actions={
        <>
          <Button variant="primary" size="lg" onClick={() => playSet(tracks)}>
            {playing ? <PauseIcon /> : <PlayIcon />}
            {playing ? t("action.pause") : t("action.play")}
          </Button>
          <Button variant="outline" size="lg" onClick={shuffle}>
            <ShuffleIcon size={16} />
            {t("action.shuffle")}
          </Button>
        </>
      }
      tracks={tracks}
      href={href}
      currentTrackId={currentTrackId ?? null}
      isPlaying={isPlaying}
      onPlayTrack={(track) => playTrack(track, tracks)}
      size="feature"
    />
  );
}

/** Ниже этого числа плиток сетка выглядит обрывком, и лучше показать треки как есть. */
const MIN_DISTINCT = 3;

/**
 * Один трек на альбом. Импорт сборника даёт треки десятками подряд, и первый экран превращался
 * в стену из одной обложки. Треки без альбома проходят как есть: их нечем схлопывать.
 */
function onePerAlbum(tracks: Track[]): Track[] {
  const seen = new Set<string>();

  const distinct = tracks.filter((track) => {
    if (!track.albumId) return true;
    if (seen.has(track.albumId)) return false;

    seen.add(track.albumId);
    return true;
  });

  return distinct.length >= MIN_DISTINCT ? distinct : tracks;
}
