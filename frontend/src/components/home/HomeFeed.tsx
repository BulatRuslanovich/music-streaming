// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { HomeBlock, HomeBlockLayout, Track } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { AlbumCard, ArtistCard, PlaylistCard, TrackCards } from "../MediaCard";
import { SectionHeader, Shelf } from "../PageHeader";
import { blockHref, blockOrigin, blockTitle, QUICK_TILES } from "./blockMeta";
import { ChartBlock } from "./ChartBlock";
import { DjLauncher } from "./DjLauncher";
import { FavoritesTile } from "./FavoritesTile";
import { HeroBlock } from "./HeroBlock";
import { NewArrivalsGrid } from "./NewArrivalsGrid";
import { QuickTiles } from "./QuickTiles";
import { QuickRow } from "./Tile";

const TILED: ReadonlySet<HomeBlockLayout> = new Set<HomeBlockLayout>(["Tile", "QuickTiles"]);

export function HomeFeed({ blocks }: { blocks: HomeBlock[] }) {
  const hero = blocks.find((block) => block.layout === "Hero");
  const fallbackTracks = uniqueTracks(blocks.flatMap((block) => block.tracks ?? []));
  const lead: HomeBlock | undefined =
    hero ??
    (fallbackTracks.length > 0
      ? {
          key: "focusLead",
          baseKey: QUICK_TILES,
          layout: "Hero",
          tracks: fallbackTracks,
        }
      : undefined);
  const secondary = hero ? blocks.filter((block) => block.key !== hero.key) : blocks;
  const grouped = runs(secondary);
  const startsWithTiles = grouped.length > 0 && TILED.has(grouped[0][0].layout);

  return (
    <>
      {lead && <Block block={lead} />}
      {!startsWithTiles && (
        <QuickRow>
          <DjLauncher tracks={fallbackTracks} />
        </QuickRow>
      )}
      {grouped.map((run, index) =>
        TILED.has(run[0].layout) ? (
          <QuickRow key={run[0].key}>
            {index === 0 && <DjLauncher tracks={fallbackTracks} />}
            {run.map((block) => (
              <Tiles key={block.key} block={block} />
            ))}
          </QuickRow>
        ) : (
          <Block key={run[0].key} block={run[0]} />
        ),
      )}
    </>
  );
}

function uniqueTracks(tracks: Track[]): Track[] {
  return [...new Map(tracks.map((track) => [track.id, track])).values()];
}

function runs(blocks: HomeBlock[]): HomeBlock[][] {
  const grouped: HomeBlock[][] = [];

  for (const block of blocks) {
    const previous = grouped.at(-1);

    if (TILED.has(block.layout) && previous && TILED.has(previous[0].layout)) previous.push(block);
    else grouped.push([block]);
  }

  return grouped;
}

function Tiles({ block }: { block: HomeBlock }) {
  if (block.layout === "Tile") return <FavoritesTile block={block} />;

  return <QuickTiles block={block} origin={blockOrigin(block)} />;
}

function Block({ block }: { block: HomeBlock }) {
  const t = useT();

  const title = blockTitle(block, t);
  const href = blockHref(block);
  const origin = blockOrigin(block);

  if (block.layout === "Hero") {
    return <HeroBlock block={block} title={title} href={href} origin={origin} />;
  }

  if (block.layout === "Grid" || block.layout === "Chart") {
    return (
      <section className="flex flex-col gap-3">
        <SectionHeader title={title} href={href} />
        {block.layout === "Grid" ? (
          <NewArrivalsGrid block={block} origin={origin} />
        ) : (
          <ChartBlock block={block} origin={origin} />
        )}
      </section>
    );
  }

  return (
    <Shelf title={title} href={href}>
      <ShelfItems block={block} origin={origin} />
    </Shelf>
  );
}

function ShelfItems({
  block,
  origin,
}: {
  block: HomeBlock;
  origin: ReturnType<typeof blockOrigin>;
}) {
  if (block.artists?.length) {
    return (
      <>
        {block.artists.map((artist) => (
          <ArtistCard key={artist.id} artist={artist} />
        ))}
      </>
    );
  }

  if (block.albums?.length) {
    return (
      <>
        {block.albums.map((album) => (
          <AlbumCard key={album.id} album={album} />
        ))}
      </>
    );
  }

  if (block.playlists?.length) {
    return (
      <>
        {block.playlists.map((playlist) => (
          <PlaylistCard key={playlist.id} playlist={playlist} />
        ))}
      </>
    );
  }

  const tracks = block.tracks ?? [];

  return <TrackCards tracks={tracks} context={tracks} origin={origin} />;
}
