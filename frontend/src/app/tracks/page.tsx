"use client";

import { useState } from "react";
import { api, type TrackSort } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { usePlayer } from "@/contexts/PlayerContext";
import { TrackList } from "@/components/TrackList";
import { LoadError, PageHeader, Pagination, PlayAllButton, Skeleton } from "@/components/ui";

const PAGE_SIZE = 100;

const sortLabels: Record<TrackSort, string> = {
  Title: "Title",
  Recent: "Recently added",
  Artist: "Artist",
  Album: "Album",
};

export default function TracksPage() {
  const [sort, setSort] = useState<TrackSort>("Title");
  const [page, setPage] = useState(1);
  const player = usePlayer();

  const { data, error, loading, reload } = useApi(
    () => api.tracks({ page, pageSize: PAGE_SIZE, sort }),
    [page, sort],
  );

  return (
    <>
      <PageHeader
        title="Tracks"
        subtitle={data ? `${data.total.toLocaleString()} tracks in your library` : undefined}
        actions={
          <>
            <label className="select-field">
              <span className="sr-only">Sort by</span>
              <select
                value={sort}
                onChange={(event) => {
                  setSort(event.target.value as TrackSort);
                  setPage(1);
                }}
              >
                {Object.entries(sortLabels).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </label>

            {data && data.items.length > 0 && (
              <>
                <PlayAllButton tracks={data.items} />
                <button
                  type="button"
                  className="button"
                  onClick={() => {
                    if (!player.shuffle) player.toggleShuffle();
                    player.playQueue(data.items, Math.floor(Math.random() * data.items.length));
                  }}
                >
                  Shuffle
                </button>
              </>
            )}
          </>
        }
      />

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton variant="row" count={12} />}

      {data && (
        <>
          <TrackList
            tracks={data.items}
            onChanged={reload}
            emptyMessage="No tracks yet. Upload some MP3 files to get started."
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
