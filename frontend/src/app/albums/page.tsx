"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { usePagedApi } from "@/lib/usePagedApi";
import { AlbumCard, LoadError, PageHeader, Pagination, Skeleton } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 60;

export default function AlbumsPage() {
  const t = useT();

  const [recentFirst, setRecentFirst] = useState(false);

  const { data, error, loading, reload, setPage } = usePagedApi(
    (page) => api.albums({ page, pageSize: PAGE_SIZE, recentFirst }),
    [recentFirst],
    "albums",
  );

  return (
    <>
      <PageHeader
        title={t("nav.albums")}
        subtitle={data ? t("count.albums", { count: data.total }) : undefined}
        actions={
          <label className="select-field">
            <span className="sr-only">{t("sort.label")}</span>
            <select
              value={recentFirst ? "recent" : "title"}
              onChange={(event) => setRecentFirst(event.target.value === "recent")}
            >
              <option value="title">{t("sort.title")}</option>
              <option value="recent">{t("sort.dateAdded")}</option>
            </select>
          </label>
        }
      />

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton count={12} />}

      {data && (
        <>
          {data.items.length === 0 ? (
            <p className="empty-state">{t("albums.empty")}</p>
          ) : (
            <div className="card-grid">
              {data.items.map((album) => (
                <AlbumCard key={album.id} album={album} />
              ))}
            </div>
          )}

          <Pagination result={data} onChange={setPage} />
        </>
      )}
    </>
  );
}
