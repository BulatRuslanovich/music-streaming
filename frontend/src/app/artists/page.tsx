"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { usePagedApi } from "@/lib/usePagedApi";
import { SearchField } from "@/components/SearchField";
import { ArtistCard, LoadError, PageHeader, Pagination, Skeleton } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 60;

export default function ArtistsPage() {
  const t = useT();
  const [search, setSearch] = useState("");

  const { data, error, loading, reload, setPage } = usePagedApi(
    (page) => api.artists({ page, pageSize: PAGE_SIZE, q: search || undefined }),
    [search],
    "artists",
  );

  return (
    <>
      <PageHeader
        title={t("nav.artists")}
        subtitle={data ? t("count.artists", { count: data.total }) : undefined}
      />

      <div className="page-tools">
        <SearchField value={search} onChange={setSearch} placeholder={t("filter.artists")} />
      </div>

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton count={12} />}

      {data && (
        <>
          {data.items.length === 0 ? (
            <p className="empty-state">
              {search ? t("filter.nothingMatched") : t("artists.empty")}
            </p>
          ) : (
            <div className="card-grid">
              {data.items.map((artist) => (
                <ArtistCard key={artist.id} artist={artist} />
              ))}
            </div>
          )}

          <Pagination result={data} onChange={setPage} />
        </>
      )}
    </>
  );
}
