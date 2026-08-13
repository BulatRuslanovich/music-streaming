"use client";

import Link from "next/link";
import { api } from "@/lib/api";
import { useFormat } from "@/lib/useFormat";
import { usePagedApi } from "@/lib/usePagedApi";
import { TrackList } from "@/components/TrackList";
import { EmptyState, LoadError, PageHeader, Pagination, PlayAllButton, Skeleton } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 100;

export default function FavoritesPage() {
  const t = useT();
  const format = useFormat();

  const { data, error, loading, reload, setPage } = usePagedApi(
    (page) => api.favorites({ page, pageSize: PAGE_SIZE }),
    [],
    "favorites",
  );

  const wholeListLoaded = data !== null && data.total <= data.items.length;
  const totalDuration = wholeListLoaded
    ? data.items.reduce((sum, track) => sum + track.durationSeconds, 0)
    : 0;

  return (
    <>
      <PageHeader
        title={t("nav.favorites")}
        subtitle={
          data
            ? t("count.tracks", { count: data.total }) +
              (totalDuration > 0 ? ` · ${format.totalDuration(totalDuration)}` : "")
            : undefined
        }
        actions={data && data.items.length > 0 ? <PlayAllButton tracks={data.items} /> : undefined}
      />

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton variant="row" count={8} />}

      {data && data.total === 0 && (
        <EmptyState
          title={t("favorites.emptyTitle")}
          description={t("favorites.emptyDescription")}
          action={
            <Link href="/tracks" className="button button-primary">
              {t("favorites.browseTracks")}
            </Link>
          }
        />
      )}

      {data && data.total > 0 && (
        <>
          <TrackList tracks={data.items} onChanged={reload} origin={{ source: "favorites" }} />
          <Pagination result={data} onChange={setPage} />
        </>
      )}
    </>
  );
}
