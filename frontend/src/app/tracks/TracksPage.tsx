// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { useCallback, useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { TrackSort } from "@/lib/types";
import type { TranslationKey } from "@/lib/i18n";
import { TRACK_PAGE_SIZE } from "@/lib/pageSizes";
import { queries } from "@/lib/queries";
import { usePage } from "@/lib/usePage";
import { useInvalidate } from "@/lib/useInvalidate";
import { useRowSelection } from "@/lib/useRowSelection";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { usePlayer } from "@/contexts/PlayerContext";
import { useToast } from "@/contexts/ToastContext";
import { PageHeader, Section } from "@/components/PageHeader";
import { Pagination, PageToolbar, SortSelect } from "@/components/PageToolbar";
import { PlayAllButton } from "@/components/PlayAllButton";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { Button } from "@/components/ui/button";
import { CheckIcon, ShuffleIcon } from "lucide-react";
import { useConfirm } from "@/components/ui/alert-dialog";

const sortKeys: Record<TrackSort, TranslationKey> = {
  Title: "sort.title",
  Recent: "sort.dateAdded",
  Artist: "sort.artist",
  Album: "sort.album",
};

export function TracksPage() {
  const t = useT();
  const player = usePlayer();
  const { isAdmin } = useAuth();
  const [confirm, confirmDialog] = useConfirm();

  const [sort, setSort] = useState<TrackSort>("Title");
  const [search, setSearch] = useState("");
  const [page, setPage] = usePage([sort, search]);

  const tracks = useQuery(
    queries.tracks({ page, pageSize: TRACK_PAGE_SIZE, sort, q: search || undefined }),
  );

  const items = useMemo(() => tracks.data?.items ?? [], [tracks.data]);
  const ids = useMemo(() => items.map((track) => track.id), [items]);

  const selection = useRowSelection(ids, `${page}:${sort}:${search}`);

  // Режим выбора выключен по умолчанию: иначе колонка чекбоксов навсегда съедает
  // номер трека и кнопку воспроизведения по ховеру у всех админов.
  const [selecting, setSelecting] = useState(false);
  const { clear } = selection;

  const stopSelecting = useCallback(() => {
    setSelecting(false);
    clear();
  }, [clear]);

  useEffect(() => {
    if (!selecting) return;

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      // Escape внутри открытого диалога принадлежит диалогу.
      if (document.querySelector('[role="dialog"], [role="alertdialog"]')) return;

      stopSelecting();
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [selecting, stopSelecting]);

  const shuffle = useMutation({
    mutationFn: () => api.shuffleTracks({ q: search || undefined }),
    onSuccess: (shuffled) => {
      if (shuffled.length === 0) return;

      if (!player.shuffle) player.toggleShuffle();
      player.playQueue(shuffled, 0);
    },
  });

  return (
    <>
      <PageHeader
        title={t("nav.tracks")}
        subtitle={
          tracks.data ? t("count.tracksInLibrary", { count: tracks.data.total }) : undefined
        }
        actions={
          items.length > 0 && (
            <>
              <PlayAllButton tracks={items} />
              <Button onClick={() => shuffle.mutate()} disabled={shuffle.isPending}>
                <ShuffleIcon size={16} />
                {shuffle.isPending ? t("action.shuffling") : t("action.shuffle")}
              </Button>
            </>
          )
        }
      />

      <PageToolbar search={search} onSearch={setSearch} placeholder={t("filter.tracks")}>
        <SortSelect value={sort} onChange={setSort} options={sortKeys} />
        {isAdmin && (
          <BulkActions
            selection={selection}
            selecting={selecting}
            onStart={() => setSelecting(true)}
            onStop={stopSelecting}
            confirm={confirm}
          />
        )}
      </PageToolbar>

      <Query result={tracks}>
        {(data) => (
          <Section>
            <TrackList
              tracks={data.items}
              emptyMessage={search ? t("filter.nothingMatched") : undefined}
              selection={
                isAdmin && selecting
                  ? {
                      selected: selection.selected,
                      onToggle: selection.toggle,
                      onToggleAll: selection.toggleAll,
                    }
                  : undefined
              }
            />

            <Pagination result={data} onChange={setPage} />
          </Section>
        )}
      </Query>

      {confirmDialog}
    </>
  );
}

function BulkActions({
  selection,
  selecting,
  onStart,
  onStop,
  confirm,
}: {
  selection: ReturnType<typeof useRowSelection>;
  selecting: boolean;
  onStart: () => void;
  onStop: () => void;
  confirm: ReturnType<typeof useConfirm>[0];
}) {
  const t = useT();
  const { notify } = useToast();
  const invalidate = useInvalidate();

  const { selected } = selection;

  const deleteSelected = useMutation({
    mutationFn: () => api.deleteTracks([...selected]),
    onSuccess: (result) => {
      onStop();
      notify(t("tracks.deletedCount", { count: result.deleted }), "success");
      invalidate("library", "playlists", "favorites", "history");
    },
  });

  if (!selecting) {
    return (
      <div className="flex min-h-9 items-center">
        <Button variant="text" size="auto" onClick={onStart}>
          <CheckIcon size={16} />
          {t("tracks.selectMode")}
        </Button>
      </div>
    );
  }

  return (
    <div className="flex min-h-9 items-center gap-2">
      <span className="text-sm text-muted-foreground tabular-nums" aria-live="polite">
        {t("tracks.selectedCount", { count: selected.size })}
      </span>

      {selected.size > 0 && (
        <Button
          variant="destructive"
          disabled={deleteSelected.isPending}
          onClick={() =>
            confirm({
              title: t("tracks.confirmBulkDelete", { count: selected.size }),
              description: t("tracks.bulkDeleteHint"),
              confirmLabel: t("action.delete"),
              destructive: true,
              action: () => deleteSelected.mutate(),
            })
          }
        >
          {t("action.delete")}
        </Button>
      )}

      <Button variant="text" size="auto" disabled={deleteSelected.isPending} onClick={onStop}>
        {t("tracks.exitSelectMode")}
      </Button>
    </div>
  );
}
