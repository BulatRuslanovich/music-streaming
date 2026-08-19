// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { queries } from "@/lib/queries";
import { usePage } from "@/lib/usePage";
import { AlbumCard } from "@/components/MediaCard";
import { CardGrid, PageHeader } from "@/components/PageHeader";
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

      <Query
        result={albums}
        skeletonCount={12}
        empty={{ title: search ? t("filter.nothingMatched") : t("albums.empty") }}
      >
        {(data) => (
          <>
            <CardGrid>
              {data.items.map((album) => (
                <AlbumCard key={album.id} album={album} />
              ))}
            </CardGrid>

            <Pagination result={data} onChange={setPage} />
          </>
        )}
      </Query>
    </>
  );
}
