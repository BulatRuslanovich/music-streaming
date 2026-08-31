// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { ArtistIcon } from "@/components/Icons";
import { useState } from "react";
import { queries } from "@/lib/queries";
import { ArtistCard } from "@/components/MediaCard";
import { CardGrid, PageHeader, Shelf } from "@/components/PageHeader";
import { Section } from "@/components/collection/Section";
import { PageToolbar } from "@/components/PageToolbar";
import { InfiniteQuery } from "@/components/InfiniteQuery";
import { useT } from "@/contexts/I18nContext";

export const ARTISTS_PAGE_SIZE = 60;

export function ArtistsPage() {
  const t = useT();

  const [search, setSearch] = useState("");

  const artists = useInfiniteQuery(
    queries.artistsFeed({ pageSize: ARTISTS_PAGE_SIZE, q: search || undefined }),
  );

  const overview = useQuery({ ...queries.libraryOverview(), enabled: !search });
  const recent = overview.data?.recentArtists ?? [];

  const total = artists.data?.pages[0]?.total ?? 0;
  const showShelf = !search && recent.length > 0 && total > recent.length;

  return (
    <>
      <PageHeader
        title={t("nav.artists")}
        subtitle={artists.data ? t("count.artists", { count: total }) : undefined}
      />

      <PageToolbar search={search} onSearch={setSearch} placeholder={t("filter.artists")} />

      {showShelf && (
        <Shelf title={t("library.recentlyAdded")}>
          {recent.map((artist) => (
            <ArtistCard key={artist.id} artist={artist} bare />
          ))}
        </Shelf>
      )}

      <InfiniteQuery
        result={artists}
        skeletonCount={12}
        empty={{
          icon: <ArtistIcon size={24} />,
          title: search ? t("filter.nothingMatched") : t("artists.empty"),
        }}
      >
        {(items) => (
          <Section title={showShelf ? t("library.allArtists") : t("nav.artists")}>
            {/* `bare`, как и на полке выше: одна и та же сущность не должна выглядеть
                двумя способами на одном экране. Круглые аватары везде без подложки. */}
            <CardGrid>
              {items.map((artist) => (
                <ArtistCard key={artist.id} artist={artist} bare />
              ))}
            </CardGrid>
          </Section>
        )}
      </InfiniteQuery>
    </>
  );
}
