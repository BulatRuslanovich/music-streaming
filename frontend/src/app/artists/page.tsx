// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { queries } from "@/lib/queries";
import { usePage } from "@/lib/usePage";
import { ArtistCard } from "@/components/MediaCard";
import { CardGrid, PageHeader, Shelf } from "@/components/PageHeader";
import { Section } from "@/components/collection/Section";
import { Pagination, PageToolbar } from "@/components/PageToolbar";
import { Query } from "@/components/Query";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 60;

export default function ArtistsPage() {
  const t = useT();

  const [search, setSearch] = useState("");
  const [page, setPage] = usePage([search]);

  const artists = useQuery(queries.artists({ page, pageSize: PAGE_SIZE, q: search || undefined }));

  const overview = useQuery({ ...queries.libraryOverview(), enabled: !search });
  const recent = overview.data?.recentArtists ?? [];

  // Полка бессмысленна, пока вся фонотека и так помещается в неё: она просто повторила бы сетку.
  const showShelf = !search && recent.length > 0 && (artists.data?.total ?? 0) > recent.length;

  return (
    <>
      <PageHeader
        title={t("nav.artists")}
        subtitle={artists.data ? t("count.artists", { count: artists.data.total }) : undefined}
      />

      <PageToolbar search={search} onSearch={setSearch} placeholder={t("filter.artists")} />

      {showShelf && (
        <Shelf title={t("library.recentlyAdded")}>
          {recent.map((artist) => (
            <ArtistCard key={artist.id} artist={artist} bare />
          ))}
        </Shelf>
      )}

      <Query
        result={artists}
        skeletonCount={12}
        empty={{ title: search ? t("filter.nothingMatched") : t("artists.empty") }}
      >
        {(data) => (
          <Section title={showShelf ? t("library.allArtists") : t("nav.artists")}>
            <CardGrid>
              {data.items.map((artist) => (
                <ArtistCard key={artist.id} artist={artist} />
              ))}
            </CardGrid>

            <Pagination result={data} onChange={setPage} />
          </Section>
        )}
      </Query>
    </>
  );
}
