"use client";

import Link from "next/link";
import { useState } from "react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { useToast } from "@/contexts/ToastContext";
import { TrackList } from "@/components/TrackList";
import { EmptyState, LoadError, PageHeader, Pagination, PlayAllButton, Skeleton } from "@/components/ui";

const PAGE_SIZE = 100;

export default function RecentlyPlayedPage() {
  const [page, setPage] = useState(1);
  const { notify, notifyError } = useToast();

  // The distinct view (one row per track, newest play first) is what a listener expects here.
  const recent = useApi(() => api.recentlyPlayed({ page, pageSize: PAGE_SIZE }), [page]);
  // The raw log supplies the "played" timestamps shown beside each row.
  const log = useApi(() => api.history({ pageSize: PAGE_SIZE }), [page]);

  const playedAt: Record<string, string> = {};
  for (const entry of log.data?.items ?? []) {
    playedAt[entry.track.id] ??= entry.playedAt;
  }

  const clear = async () => {
    if (!window.confirm("Clear your entire listening history?")) return;

    try {
      await api.clearHistory();
      notify("Listening history cleared.", "success");
      recent.reload();
      log.reload();
    } catch (reason) {
      notifyError(reason, "Could not clear the history.");
    }
  };

  return (
    <>
      <PageHeader
        title="Recently played"
        subtitle={recent.data ? `${recent.data.total.toLocaleString()} tracks played` : undefined}
        actions={
          <>
            {recent.data && recent.data.items.length > 0 && <PlayAllButton tracks={recent.data.items} />}
            {recent.data && recent.data.total > 0 && (
              <button type="button" className="button" onClick={() => void clear()}>
                Clear history
              </button>
            )}
          </>
        }
      />

      {recent.error && <LoadError message={recent.error} onRetry={recent.reload} />}
      {recent.loading && !recent.data && <Skeleton variant="row" count={8} />}

      {recent.data && recent.data.total === 0 && (
        <EmptyState
          title="Nothing played yet"
          description="A track is recorded here once you have listened to it for long enough."
          action={
            <Link href="/" className="button button-primary">
              Find something to play
            </Link>
          }
        />
      )}

      {recent.data && recent.data.total > 0 && (
        <>
          <TrackList tracks={recent.data.items} playedAt={playedAt} onChanged={recent.reload} />
          <Pagination
            page={recent.data.page}
            totalPages={recent.data.totalPages}
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
