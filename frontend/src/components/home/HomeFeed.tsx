// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { Artist, HomeBlock, Track } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { AlbumCard, ArtistCard, PlaylistCard, TrackCards } from "../MediaCard";
import { SectionHeader, Shelf } from "../PageHeader";
import { blockHref, blockOrigin, blockTitle } from "./blockMeta";
import { ChartBlock } from "./ChartBlock";
import { FavoritesTile } from "./FavoritesTile";
import { HeroBlock } from "./HeroBlock";
import { NewArrivalsGrid } from "./NewArrivalsGrid";
import { QuickTiles } from "./QuickTiles";
import { RadioRow } from "./RadioRow";
import { QuickRow } from "./Tile";

/**
 * Главная состоит из трёх полос разного веса. Зону выбирает бэкенд (`HomeZone`), поэтому здесь
 * остаётся только разложить блоки по полосам — угадывать замысел по порядку больше не нужно.
 */
export function HomeFeed({ blocks }: { blocks: HomeBlock[] }) {
  const t = useT();

  const lead = blocks.filter((block) => block.zone === "Lead");
  const quick = blocks.filter((block) => block.zone === "Quick");
  const browse = blocks.filter((block) => block.zone === "Browse");

  // Мозаики радиостанций собираются из всего, что уже приехало на страницу.
  const artwork = uniqueTracks(blocks.flatMap((block) => block.tracks ?? []));

  return (
    <>
      {lead.length > 0 && (
        <div className="flex flex-col gap-4">
          {lead.map((block) => (
            <Block key={block.key} block={block} />
          ))}
        </div>
      )}

      <section className="flex flex-col gap-3">
        <SectionHeader eyebrow={t("home.radioEyebrow")} title={t("home.radioTitle")} />
        <RadioRow tracks={artwork} />
      </section>

      {quick.length > 0 && (
        <section className="group/section flex flex-col gap-3">
          <SectionHeader title={t("home.quickPicks")} href="/recently-played" />
          <QuickRow>
            {quick.map((block) => (
              <Tiles key={block.key} block={block} />
            ))}
          </QuickRow>
        </section>
      )}

      {browse.map((block) => (
        <Block key={block.key} block={block} />
      ))}
    </>
  );
}

function uniqueTracks(tracks: Track[]): Track[] {
  return [...new Map(tracks.map((track) => [track.id, track])).values()];
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
      <section className="group/section flex flex-col gap-3">
        <SectionHeader title={title} href={href} />
        {block.layout === "Grid" ? (
          <NewArrivalsGrid block={block} origin={origin} />
        ) : (
          <ChartBlock block={block} origin={origin} />
        )}
      </section>
    );
  }

  if (block.layout === "Circles") {
    return (
      <Shelf title={title} href={href}>
        <ArtistCircles artists={block.artists ?? []} />
      </Shelf>
    );
  }

  return (
    <Shelf title={title} href={href}>
      <ShelfItems block={block} origin={origin} />
    </Shelf>
  );
}

function ArtistCircles({ artists }: { artists: Artist[] }) {
  return (
    <>
      {artists.map((artist) => (
        <ArtistCard key={artist.id} artist={artist} bare />
      ))}
    </>
  );
}

function ShelfItems({
  block,
  origin,
}: {
  block: HomeBlock;
  origin: ReturnType<typeof blockOrigin>;
}) {
  if (block.artists?.length) return <ArtistCircles artists={block.artists} />;

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
