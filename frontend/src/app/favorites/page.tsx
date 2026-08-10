"use client";

import Link from "next/link";
import { useState } from "react";
import { api } from "@/lib/api";
import { formatTotalDuration } from "@/lib/format";
import { useApi } from "@/lib/useApi";
import { TrackList } from "@/components/TrackList";
import { EmptyState, LoadError, PageHeader, Pagination, PlayAllButton, Skeleton } from "@/components/ui";

const PAGE_SIZE = 100;

export default function FavoritesPage() {
  const [page, setPage] = useState(1);
  const { data, error, loading, reload } = useApi(
    () => api.favorites({ page, pageSize: PAGE_SIZE }),
    [page],
  );

  const totalDuration = data?.items.reduce((sum, track) => sum + track.durationSeconds, 0) ?? 0;

  return (
    <>
      <PageHeader
        title="Favourites"
        subtitle={
          data
            ? `${data.total.toLocaleString()} track${data.total === 1 ? "" : "s"}` +
              (totalDuration > 0 ? ` · ${formatTotalDuration(totalDuration)}` : "")
            : undefined
        }
        actions={data && data.items.length > 0 ? <PlayAllButton tracks={data.items} /> : undefined}
      />

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton variant="row" count={8} />}

      {data && data.total === 0 && (
        <EmptyState
          title="No favourites yet"
          description="Tap the heart next to any track and it will show up here."
          action={
            <Link href="/tracks" className="button button-primary">
              Browse tracks
            </Link>
          }
        />
      )}

      {data && data.total > 0 && (
        <>
          {/* Un-favouriting from this page should drop the row, so a reload follows the change. */}
          <TrackList tracks={data.items} onChanged={reload} />
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
