// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { useCallback, useMemo, useRef, useState } from "react";
import { api, type TrackSort } from "@/lib/api";
import type { TranslationKey } from "@/lib/i18n";
import { queries } from "@/lib/queries";
import { usePage } from "@/lib/usePage";
import { useInvalidate } from "@/lib/useInvalidate";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { usePlayer } from "@/contexts/PlayerContext";
import { useToast } from "@/contexts/ToastContext";
import { PageHeader } from "@/components/PageHeader";
import { Pagination, PageToolbar, SortSelect } from "@/components/PageToolbar";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/alert-dialog";

const PAGE_SIZE = 100;

const sortKeys: Record<TrackSort, TranslationKey> = {
  Title: "sort.title",
  Recent: "sort.dateAdded",
  Artist: "sort.artist",
  Album: "sort.album",
};

export default function TracksPage() {
  const t = useT();
  const player = usePlayer();
  const { isAdmin } = useAuth();
  const { notify, notifyError } = useToast();
  const invalidate = useInvalidate();
  const [confirm, confirmDialog] = useConfirm();

  const [sort, setSort] = useState<TrackSort>("Title");
  const [search, setSearch] = useState("");
  const [shuffling, setShuffling] = useState(false);
  const [page, setPage] = usePage([sort, search]);

  const tracks = useQuery(
    queries.tracks({ page, pageSize: PAGE_SIZE, sort, q: search || undefined }),
  );

  const items = useMemo(() => tracks.data?.items ?? [], [tracks.data]);

  const [selected, setSelected] = useState<ReadonlySet<string>>(() => new Set());
  const [deleting, setDeleting] = useState(false);

  const listKey = `${page}:${sort}:${search}`;
  const [selectionKey, setSelectionKey] = useState(listKey);
  if (selectionKey !== listKey) {
    setSelectionKey(listKey);
    setSelected(new Set());
  }

  const anchor = useRef<{ key: string; index: number } | null>(null);

  const toggle = useCallback(
    (id: string, index: number, extend: boolean) => {
      const from = extend && anchor.current?.key === listKey ? anchor.current.index : index;

      setSelected((current) => {
        const next = new Set(current);
        const turningOn = !next.has(id);

        for (let at = Math.min(from, index); at <= Math.max(from, index); at += 1) {
          const trackId = items[at]?.id;
          if (!trackId) continue;

          if (turningOn) next.add(trackId);
          else next.delete(trackId);
        }

        return next;
      });

      anchor.current = { key: listKey, index };
    },
    [items, listKey],
  );

  const toggleAll = useCallback(() => {
    setSelected((current) =>
      current.size === items.length ? new Set() : new Set(items.map((track) => track.id)),
    );
    anchor.current = null;
  }, [items]);

  const deleteSelected = async () => {
    setDeleting(true);
    try {
      const result = await api.deleteTracks([...selected]);

      setSelected(new Set());
      anchor.current = null;

      notify(t("tracks.deletedCount", { count: result.deleted }), "success");
      invalidate("library", "playlists", "favorites", "history");
    } catch (failure) {
      notifyError(failure, t("tracks.bulkDeleteFailed"));
    } finally {
      setDeleting(false);
    }
  };

  const shuffle = async () => {
    setShuffling(true);
    try {
      const shuffled = await api.shuffleTracks({ q: search || undefined });
      if (shuffled.length === 0) return;

      if (!player.shuffle) player.toggleShuffle();
      player.playQueue(shuffled, 0, { source: "tracks" });
    } catch (failure) {
      notifyError(failure, t("tracks.shuffleFailed"));
    } finally {
      setShuffling(false);
    }
  };

  return (
    <>
      <PageHeader
        title={t("nav.tracks")}
        subtitle={
          tracks.data ? t("count.tracksInLibrary", { count: tracks.data.total }) : undefined
        }
        actions={
          tracks.data && tracks.data.total > 0 ? (
            <Button onClick={() => void shuffle()} disabled={shuffling}>
              {shuffling ? t("action.shuffling") : t("action.shuffle")}
            </Button>
          ) : undefined
        }
      />

      <PageToolbar
        search={search}
        onSearch={setSearch}
        placeholder={t("filter.tracks")}
        sort={<SortSelect value={sort} onChange={setSort} options={sortKeys} />}
      >
        {isAdmin && (
          <div className="flex min-h-9 items-center gap-2">
            {selected.size > 0 && (
              <>
                <span className="text-sm text-muted-foreground tabular-nums" aria-live="polite">
                  {t("tracks.selectedCount", { count: selected.size })}
                </span>

                <Button
                  variant="text"
                  size="auto"
                  disabled={deleting}
                  onClick={() => setSelected(new Set())}
                >
                  {t("action.clear")}
                </Button>

                <Button
                  variant="destructive"
                  disabled={deleting}
                  onClick={() =>
                    confirm({
                      title: t("tracks.confirmBulkDelete", { count: selected.size }),
                      description: t("tracks.bulkDeleteHint"),
                      confirmLabel: t("action.delete"),
                      destructive: true,
                      action: () => void deleteSelected(),
                    })
                  }
                >
                  {t("action.delete")}
                </Button>
              </>
            )}
          </div>
        )}
      </PageToolbar>

      <Query result={tracks} skeleton="row" skeletonCount={12}>
        {(data) => (
          <>
            <TrackList
              tracks={data.items}
              origin={{ source: "tracks" }}
              emptyMessage={search ? t("filter.nothingMatched") : undefined}
              selection={
                isAdmin ? { selected, onToggle: toggle, onToggleAll: toggleAll } : undefined
              }
            />

            <Pagination result={data} onChange={setPage} />
          </>
        )}
      </Query>

      {confirmDialog}
    </>
  );
}
