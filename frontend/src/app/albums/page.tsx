// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { queries } from "@/lib/queries";
import { usePage } from "@/lib/usePage";
import { AlbumCard } from "@/components/MediaCard";
import { CardGrid, PageHeader, Shelf } from "@/components/PageHeader";
import { Section } from "@/components/collection/Section";
import { Pagination, PageToolbar, SortSelect } from "@/components/PageToolbar";
import { Query } from "@/components/Query";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 60;

const sortKeys = { title: "sort.title", recent: "sort.dateAdded" } as const;

type Sort = keyof typeof sortKeys;

export default function AlbumsPage() {
  const t = useT();

  const [sort, setSort] = useState<Sort>("title");
  const [search, setSearch] = useState("");
  const [page, setPage] = usePage([sort, search]);

  const albums = useQuery(
    queries.albums({
      page,
      pageSize: PAGE_SIZE,
      recentFirst: sort === "recent",
      q: search || undefined,
    }),
  );

  // Полка дублировала бы сетку при поиске и при сортировке «сначала новые», поэтому в этих
  // режимах её нет — верхний контекст осмыслен только над алфавитным списком целиком. И она
  // бессмысленна, пока вся фонотека помещается в саму полку.
  const alphabetical = !search && sort === "title";
  const overview = useQuery({ ...queries.libraryOverview(), enabled: alphabetical });
  const recent = overview.data?.recentAlbums ?? [];
  const showShelf = alphabetical && recent.length > 0 && (albums.data?.total ?? 0) > recent.length;

  return (
    <>
      <PageHeader
        title={t("nav.albums")}
        subtitle={albums.data ? t("count.albums", { count: albums.data.total }) : undefined}
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

      <Query
        result={albums}
        skeletonCount={12}
        empty={{ title: search ? t("filter.nothingMatched") : t("albums.empty") }}
      >
        {(data) => (
          <Section title={showShelf ? t("library.allAlbums") : t("nav.albums")}>
            <CardGrid>
              {data.items.map((album) => (
                <AlbumCard key={album.id} album={album} />
              ))}
            </CardGrid>

            <Pagination result={data} onChange={setPage} />
          </Section>
        )}
      </Query>
    </>
  );
}
