"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { ArtistCard, LoadError, PageHeader, Pagination, Skeleton } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 60;

export default function ArtistsPage() {
  const t = useT();

  const [page, setPage] = useState(1);
  const { data, error, loading, reload } = useApi(
    () => api.artists({ page, pageSize: PAGE_SIZE }),
    [page],
  );

  return (
    <>
      <PageHeader
        title={t("nav.artists")}
        subtitle={data ? t("count.artists", { count: data.total }) : undefined}
      />

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton count={12} />}

      {data && (
        <>
          {data.items.length === 0 ? (
            <p className="empty-state">{t("artists.empty")}</p>
          ) : (
            <div className="card-grid">
              {data.items.map((artist) => (
                <ArtistCard key={artist.id} artist={artist} />
              ))}
            </div>
          )}

          <Pagination
            page={data.page}
            totalPages={data.totalPages}
            onChange={(next) => {
              setPage(next);
              window.scrollTo({ top: 0 });
            }}
          />
        </>
      )}
    </>
  );
}
