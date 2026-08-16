"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { queries } from "@/lib/queries";
import { usePage } from "@/lib/usePage";
import { ArtistCard } from "@/components/MediaCard";
import { CardGrid, PageHeader } from "@/components/PageHeader";
import { Pagination, PageToolbar } from "@/components/PageToolbar";
import { Query } from "@/components/Query";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 60;

export default function ArtistsPage() {
  const t = useT();

  const [search, setSearch] = useState("");
  const [page, setPage] = usePage([search]);

  const artists = useQuery(queries.artists({ page, pageSize: PAGE_SIZE, q: search || undefined }));

  return (
    <>
      <PageHeader
        title={t("nav.artists")}
        subtitle={artists.data ? t("count.artists", { count: artists.data.total }) : undefined}
      />

      <PageToolbar search={search} onSearch={setSearch} placeholder={t("filter.artists")} />

      <Query
        result={artists}
        skeletonCount={12}
        empty={{ title: search ? t("filter.nothingMatched") : t("artists.empty") }}
      >
        {(data) => (
          <>
            <CardGrid>
              {data.items.map((artist) => (
                <ArtistCard key={artist.id} artist={artist} />
              ))}
            </CardGrid>

            <Pagination result={data} onChange={setPage} />
          </>
        )}
      </Query>
    </>
  );
}
