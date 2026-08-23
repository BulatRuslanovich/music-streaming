// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { api, type TrackSort } from "@/lib/api";
import type { TranslationKey } from "@/lib/i18n";
import { trackCoverUrl } from "@/lib/media";
import { queries } from "@/lib/queries";
import { useCoverColor } from "@/lib/useCoverColor";
import { useFormat } from "@/lib/useFormat";
import { usePage } from "@/lib/usePage";
import { useInvalidate } from "@/lib/useInvalidate";
import { useRowSelection } from "@/lib/useRowSelection";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { usePlayer } from "@/contexts/PlayerContext";
import { useToast } from "@/contexts/ToastContext";
import { CoverMosaic } from "@/components/collection/CoverMosaic";
import { Section } from "@/components/collection/Section";
import { Spotlight } from "@/components/collection/Spotlight";
import { PageHeader } from "@/components/PageHeader";
import { Pagination, PageToolbar, SortSelect } from "@/components/PageToolbar";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { Button } from "@/components/ui/button";
import { PlayIcon, ShuffleIcon } from "@/components/Icons";
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
  const format = useFormat();
  const player = usePlayer();
  const { isAdmin } = useAuth();
  const { notifyError } = useToast();
  const [confirm, confirmDialog] = useConfirm();

  const [sort, setSort] = useState<TrackSort>("Title");
  const [search, setSearch] = useState("");
  const [shuffling, setShuffling] = useState(false);
  const [page, setPage] = usePage([sort, search]);

  const tracks = useQuery(
    queries.tracks({ page, pageSize: PAGE_SIZE, sort, q: search || undefined }),
  );

  const items = useMemo(() => tracks.data?.items ?? [], [tracks.data]);
  const ids = useMemo(() => items.map((track) => track.id), [items]);

  const selection = useRowSelection(ids, `${page}:${sort}:${search}`);

  const overview = useQuery({ ...queries.libraryOverview(), enabled: !search });
  const stats = overview.data?.stats;
  const lead = overview.data?.recentTracks ?? [];
  const tint = useCoverColor(trackCoverUrl(lead[0], "thumb"));

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

  const playAll = () => {
    if (items.length === 0) return;
    player.playQueue(items, 0, { source: "tracks" });
  };

  return (
    <>
      <PageHeader
        title={t("nav.tracks")}
        subtitle={
          tracks.data ? t("count.tracksInLibrary", { count: tracks.data.total }) : undefined
        }
      />

      {!search && lead.length > 0 && (
        <Spotlight
          headingId="library-spotlight-heading"
          eyebrow={t("nav.library")}
          title={t("library.wholeLibrary")}
          tint={tint}
          art={<CoverMosaic tracks={lead} />}
          facts={
            stats
              ? `${t("count.tracks", { count: stats.trackCount })} · ${format.totalDuration(stats.totalDurationSeconds)}`
              : undefined
          }
          actions={
            <>
              <Button variant="primary" size="lg" onClick={playAll}>
                <PlayIcon size={20} />
                {t("action.play")}
              </Button>
              <Button
                variant="secondary"
                size="lg"
                onClick={() => void shuffle()}
                disabled={shuffling}
              >
                <ShuffleIcon size={18} />
                {shuffling ? t("action.shuffling") : t("action.shuffle")}
              </Button>
            </>
          }
        />
      )}

      <PageToolbar
        search={search}
        onSearch={setSearch}
        placeholder={t("filter.tracks")}
        sort={<SortSelect value={sort} onChange={setSort} options={sortKeys} />}
      >
        {isAdmin && <BulkActions selection={selection} confirm={confirm} />}
      </PageToolbar>

      <Query result={tracks} skeleton="row" skeletonCount={12}>
        {(data) => (
          <Section title={search ? t("nav.tracks") : t("library.allTracks")}>
            <TrackList
              tracks={data.items}
              origin={{ source: "tracks" }}
              emptyMessage={search ? t("filter.nothingMatched") : undefined}
              selection={
                isAdmin
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
  confirm,
}: {
  selection: ReturnType<typeof useRowSelection>;
  confirm: ReturnType<typeof useConfirm>[0];
}) {
  const t = useT();
  const { notify, notifyError } = useToast();
  const invalidate = useInvalidate();
  const [deleting, setDeleting] = useState(false);

  const { selected, clear } = selection;

  const deleteSelected = async () => {
    setDeleting(true);
    try {
      const result = await api.deleteTracks([...selected]);

      clear();
      notify(t("tracks.deletedCount", { count: result.deleted }), "success");
      invalidate("library", "playlists", "favorites", "history");
    } catch (failure) {
      notifyError(failure, t("tracks.bulkDeleteFailed"));
    } finally {
      setDeleting(false);
    }
  };

  return (
    <div className="flex min-h-9 items-center gap-2">
      {selected.size > 0 && (
        <>
          <span className="text-sm text-muted-foreground tabular-nums" aria-live="polite">
            {t("tracks.selectedCount", { count: selected.size })}
          </span>

          <Button variant="text" size="auto" disabled={deleting} onClick={clear}>
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
  );
}
