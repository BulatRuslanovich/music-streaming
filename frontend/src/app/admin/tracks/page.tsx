"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { TrackList } from "@/components/TrackList";
import { LoadError, PageHeader, Pagination, Skeleton } from "@/components/ui";

const PAGE_SIZE = 50;

export default function AdminTracksPage() {
  const [page, setPage] = useState(1);

  const { data, error, loading, reload } = useApi(
    () => api.tracks({ page, pageSize: PAGE_SIZE, sort: "Recent" }),
    [page],
  );

  return (
    <>
      <PageHeader
        title="Tracks"
        subtitle={data ? `${data.total.toLocaleString()} tracks, newest first` : undefined}
      />

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton variant="row" count={10} />}

      {data && (
        <>
          <p className="hint">Use a track&apos;s ⋮ menu to edit its metadata or delete it.</p>

          <TrackList
            tracks={data.items}
            showAlbum
            onChanged={reload}
            emptyMessage="No tracks yet."
          />

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
