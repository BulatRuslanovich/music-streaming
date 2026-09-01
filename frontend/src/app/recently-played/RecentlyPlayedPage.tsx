// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { api } from "@/lib/api";
import { TRACK_PAGE_SIZE } from "@/lib/pageSizes";
import { queries } from "@/lib/queries";
import { useInvalidate } from "@/lib/useInvalidate";
import { usePage } from "@/lib/usePage";
import { useToast } from "@/contexts/ToastContext";
import { PageHeader } from "@/components/PageHeader";
import { Pagination } from "@/components/PageToolbar";
import { PlayAllButton } from "@/components/PlayAllButton";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { useConfirm } from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { HistoryIcon } from "@/components/Icons";
import { useT } from "@/contexts/I18nContext";

export function RecentlyPlayedPage() {
  const t = useT();
  const { notify, notifyError } = useToast();
  const invalidate = useInvalidate();
  const [confirm, confirmDialog] = useConfirm();

  const [page, setPage] = usePage([]);
  const recent = useQuery(queries.recentlyPlayed({ page, pageSize: TRACK_PAGE_SIZE }));

  const log = useQuery(queries.history({ page, pageSize: TRACK_PAGE_SIZE }));

  const playedAt: Record<string, string> = {};
  for (const entry of log.data?.items ?? []) {
    playedAt[entry.track.id] ??= entry.playedAt;
  }

  const clear = async () => {
    try {
      await api.clearHistory();
      notify(t("recent.cleared"), "success");
      invalidate("history");
    } catch (reason) {
      notifyError(reason, t("recent.clearFailed"));
    }
  };

  const data = recent.data;

  return (
    <>
      <PageHeader
        title={t("nav.recentlyPlayed")}
        subtitle={data ? t("count.tracksPlayed", { count: data.total }) : undefined}
        actions={
          data && data.total > 0 ? (
            <>
              <PlayAllButton tracks={data.items} />
              <Button
                onClick={() =>
                  confirm({
                    title: t("recent.confirmClear"),
                    confirmLabel: t("recent.clearHistory"),
                    destructive: true,
                    action: () => void clear(),
                  })
                }
              >
                {t("recent.clearHistory")}
              </Button>
            </>
          ) : undefined
        }
      />

      <Query
        result={recent}
        skeleton="row"
        empty={{
          icon: <HistoryIcon size={24} />,
          title: t("recent.emptyTitle"),
          description: t("recent.emptyDescription"),
          action: (
            <Button variant="primary" asChild>
              <Link href="/">{t("recent.findSomething")}</Link>
            </Button>
          ),
        }}
      >
        {(result) => (
          <>
            <TrackList tracks={result.items} playedAt={playedAt} origin={{ source: "history" }} />
            <Pagination result={result} onChange={setPage} />
          </>
        )}
      </Query>

      {confirmDialog}
    </>
  );
}
