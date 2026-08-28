// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMemo, useState } from "react";
import { cn } from "@/lib/cn";
import type { Artist, HomeBlock } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { AlbumCard, ArtistCard, PlaylistCard, TrackCards } from "../MediaCard";
import { SectionHeader, Shelf } from "../PageHeader";
import { blockHref, blockOrigin, blockTitle, mosaicPool, splitMobileTail } from "./blockMeta";
import { ChartBlock } from "./ChartBlock";
import { FavoritesTile } from "./FavoritesTile";
import { HeroBlock } from "./HeroBlock";
import { NewArrivalsGrid } from "./NewArrivalsGrid";
import { QuickTiles } from "./QuickTiles";
import { RadioRow } from "./RadioRow";
import { deferredSection } from "@/components/collection/layout";
import { QuickRow } from "@/components/collection/Tile";
import { Button } from "../ui/button";

export function HomeFeed({ blocks }: { blocks: HomeBlock[] }) {
  const t = useT();

  // Хвост раскрывается в одну сторону: контрол, убирающий две тысячи пикселей из-под
  // прокрутившего пользователя, хуже той перегруженности, ради которой он заведён.
  const [expanded, setExpanded] = useState(false);

  const lead = blocks.filter((block) => block.zone === "Lead");
  const quick = blocks.filter((block) => block.zone === "Quick");
  const browse = blocks.filter((block) => block.zone === "Browse");

  const { head, tail } = useMemo(() => splitMobileTail(browse), [browse]);

  const artwork = useMemo(() => mosaicPool(blocks), [blocks]);

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

      {head.map((block) => (
        <Block key={block.key} block={block} />
      ))}

      {tail.map((block) => (
        // Секции хвоста остаются прямыми детьми фрагмента: `.stagger > *` целится в них
        // поимённо, и общая обёртка схлопнула бы четыре цели анимации в одну.
        <Block key={block.key} block={block} className={cn(!expanded && "max-md:hidden")} />
      ))}

      {tail.length > 0 && !expanded && (
        <Button
          variant="secondary"
          className="w-full md:hidden"
          aria-expanded={false}
          onClick={() => setExpanded(true)}
        >
          {t("home.showMore")}
        </Button>
      )}
    </>
  );
}

function Tiles({ block }: { block: HomeBlock }) {
  if (block.layout === "Tile") return <FavoritesTile block={block} />;

  return <QuickTiles block={block} origin={blockOrigin(block)} />;
}

function Block({ block, className }: { block: HomeBlock; className?: string }) {
  const t = useT();

  const title = blockTitle(block, t);
  const href = blockHref(block);
  const origin = blockOrigin(block);

  // Зона Browse целиком под сгибом, поэтому её секции считаются только при подъезде к экрану.
  const section = cn(block.zone === "Browse" && deferredSection, className);

  if (block.layout === "Hero") {
    return <HeroBlock block={block} title={title} href={href} origin={origin} />;
  }

  if (block.layout === "Grid" || block.layout === "Chart") {
    return (
      <section className={cn("group/section flex flex-col gap-3", section)}>
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
      <Shelf title={title} href={href} className={section}>
        <ArtistCircles artists={block.artists ?? []} />
      </Shelf>
    );
  }

  return (
    <Shelf title={title} href={href} className={section}>
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
