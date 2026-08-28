// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { queries } from "@/lib/queries";
import { AlbumCard } from "@/components/MediaCard";
import { CardGrid, PageHeader, Shelf } from "@/components/PageHeader";
import { Section } from "@/components/collection/Section";
import { PageToolbar, SortSelect } from "@/components/PageToolbar";
import { InfiniteQuery } from "@/components/InfiniteQuery";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 60;

const sortKeys = { title: "sort.title", recent: "sort.dateAdded" } as const;

type Sort = keyof typeof sortKeys;

export default function AlbumsPage() {
  const t = useT();

  const [sort, setSort] = useState<Sort>("title");
  const [search, setSearch] = useState("");

  const albums = useInfiniteQuery(
    queries.albumsFeed({
      pageSize: PAGE_SIZE,
      recentFirst: sort === "recent",
      q: search || undefined,
    }),
  );

  const alphabetical = !search && sort === "title";
  const overview = useQuery({ ...queries.libraryOverview(), enabled: alphabetical });
  const recent = overview.data?.recentAlbums ?? [];

  const total = albums.data?.pages[0]?.total ?? 0;
  const showShelf = alphabetical && recent.length > 0 && total > recent.length;

  return (
    <>
      <PageHeader
        title={t("nav.albums")}
        subtitle={albums.data ? t("count.albums", { count: total }) : undefined}
      />

      <PageToolbar
        search={search}
        onSearch={setSearch}
        placeholder={t("filter.albums")}
        sort={<SortSelect value={sort} onChange={setSort} options={sortKeys} />}
      />

      {showShelf && (
        <Shelf title={t("library.recentlyAdded")}>
          {recent.map((album) => (
            <AlbumCard key={album.id} album={album} />
          ))}
        </Shelf>
      )}

      <InfiniteQuery
        result={albums}
        skeletonCount={12}
        empty={{ title: search ? t("filter.nothingMatched") : t("albums.empty") }}
      >
        {(items) => (
          <Section title={showShelf ? t("library.allAlbums") : t("nav.albums")}>
            <CardGrid>
              {items.map((album) => (
                <AlbumCard key={album.id} album={album} />
              ))}
            </CardGrid>
          </Section>
        )}
      </InfiniteQuery>
    </>
  );
}
