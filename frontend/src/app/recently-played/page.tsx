"use client";

import Link from "next/link";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { usePagedApi } from "@/lib/usePagedApi";
import { useToast } from "@/contexts/ToastContext";
import { TrackList } from "@/components/TrackList";
import {
  EmptyState,
  LoadError,
  PageHeader,
  Pagination,
  PlayAllButton,
  Skeleton,
} from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 100;

export default function RecentlyPlayedPage() {
  const t = useT();

  const { notify, notifyError } = useToast();

  const recent = usePagedApi(
    (page) => api.recentlyPlayed({ page, pageSize: PAGE_SIZE }),
    [],
    "recentlyPlayed",
  );
  const log = useApi(
    () => api.history({ page: recent.page, pageSize: PAGE_SIZE }),
    [recent.page],
    "history",
  );

  const playedAt: Record<string, string> = {};
  for (const entry of log.data?.items ?? []) {
    playedAt[entry.track.id] ??= entry.playedAt;
  }

  const clear = async () => {
    if (!window.confirm(t("recent.confirmClear"))) return;

    try {
      await api.clearHistory();
      notify(t("recent.cleared"), "success");
      recent.reload();
      log.reload();
    } catch (reason) {
      notifyError(reason, t("recent.clearFailed"));
    }
  };

  return (
    <>
      <PageHeader
        title={t("nav.recentlyPlayed")}
        subtitle={recent.data ? t("count.tracksPlayed", { count: recent.data.total }) : undefined}
        actions={
          <>
            {recent.data && recent.data.items.length > 0 && (
              <PlayAllButton tracks={recent.data.items} />
            )}
            {recent.data && recent.data.total > 0 && (
              <button type="button" className="button" onClick={() => void clear()}>
                {t("recent.clearHistory")}
              </button>
            )}
          </>
        }
      />

      {recent.error && <LoadError message={recent.error} onRetry={recent.reload} />}
      {recent.loading && !recent.data && <Skeleton variant="row" count={8} />}

      {recent.data && recent.data.total === 0 && (
        <EmptyState
          title={t("recent.emptyTitle")}
          description={t("recent.emptyDescription")}
          action={
            <Link href="/" className="button button-primary">
              {t("recent.findSomething")}
            </Link>
          }
        />
      )}

      {recent.data && recent.data.total > 0 && (
        <>
          <TrackList
            tracks={recent.data.items}
            playedAt={playedAt}
            onChanged={recent.reload}
            origin={{ source: "history" }}
          />
          <Pagination result={recent.data} onChange={recent.setPage} />
        </>
      )}
    </>
  );
}
