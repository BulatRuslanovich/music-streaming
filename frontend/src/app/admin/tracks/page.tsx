"use client";

import { useState } from "react";
import { api, type TrackSort } from "@/lib/api";
import type { TranslationKey } from "@/lib/i18n";
import { usePagedApi } from "@/lib/usePagedApi";
import { SearchField } from "@/components/SearchField";
import { TrackList } from "@/components/TrackList";
import { LoadError, PageHeader, Pagination, Skeleton } from "@/components/ui";
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

  const { data, error, loading, reload, setPage } = usePagedApi(
    (page) => api.tracks({ page, pageSize: PAGE_SIZE, sort, q: search || undefined }),
    [sort, search],
    "adminTracks",
  );

  return (
    <>
      <PageHeader
        title={t("nav.tracks")}
        subtitle={data ? t("count.tracks", { count: data.total }) : undefined}
      />

      <div className="page-tools">
        <SearchField value={search} onChange={setSearch} placeholder={t("filter.tracks")} />

        <label className="select-field">
          <span className="sr-only">{t("sort.label")}</span>
          <select value={sort} onChange={(event) => setSort(event.target.value as TrackSort)}>
            {Object.entries(sortKeys).map(([value, key]) => (
              <option key={value} value={value}>
                {t(key)}
              </option>
            ))}
          </select>
        </label>
      </div>

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton variant="row" count={10} />}

      {data && (
        <>
          <TrackList
            tracks={data.items}
            showAlbum
            onChanged={reload}
            emptyMessage={search ? t("filter.nothingMatched") : undefined}
          />

          <Pagination result={data} onChange={setPage} />
        </>
      )}
    </>
  );
}
