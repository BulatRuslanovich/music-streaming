"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import type { TrackSort } from "@/lib/api";
import type { TranslationKey } from "@/lib/i18n";
import { queries } from "@/lib/queries";
import { usePage } from "@/lib/usePage";
import { PageHeader } from "@/components/PageHeader";
import { Pagination, PageToolbar, SortSelect } from "@/components/PageToolbar";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 50;

const sortKeys: Record<TrackSort, TranslationKey> = {
  Recent: "sort.dateAdded",
  Title: "sort.title",
  Artist: "sort.artist",
  Album: "sort.album",
};

export default function AdminTracksPage() {
  const t = useT();

  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<TrackSort>("Recent");
  const [page, setPage] = usePage([sort, search]);

  const tracks = useQuery(
    queries.tracks({ page, pageSize: PAGE_SIZE, sort, q: search || undefined }),
  );

  return (
    <>
      <PageHeader
        title={t("nav.tracks")}
        subtitle={tracks.data ? t("count.tracks", { count: tracks.data.total }) : undefined}
      />

      <PageToolbar
        search={search}
        onSearch={setSearch}
        placeholder={t("filter.tracks")}
        sort={<SortSelect value={sort} onChange={setSort} options={sortKeys} />}
      />

      <Query result={tracks} skeleton="row" skeletonCount={10}>
        {(data) => (
          <>
            <TrackList
              tracks={data.items}
              showAlbum
              emptyMessage={search ? t("filter.nothingMatched") : undefined}
            />
            <Pagination result={data} onChange={setPage} />
          </>
        )}
      </Query>
    </>
  );
}
