"use client";

import { useState } from "react";
import { api, type TrackSort } from "@/lib/api";
import type { TranslationKey } from "@/lib/i18n";
import { usePagedApi } from "@/lib/usePagedApi";
import { useT } from "@/contexts/I18nContext";
import { usePlayer } from "@/contexts/PlayerContext";
import { useToast } from "@/contexts/ToastContext";
import { SearchField } from "@/components/SearchField";
import { TrackList } from "@/components/TrackList";
import { LoadError, PageHeader, Pagination, Skeleton } from "@/components/ui";

const PAGE_SIZE = 100;

const sortKeys: Record<TrackSort, TranslationKey> = {
  Title: "sort.title",
  Recent: "sort.dateAdded",
  Artist: "sort.artist",
  Album: "sort.album",
};

export default function TracksPage() {
  const t = useT();
  const [sort, setSort] = useState<TrackSort>("Title");
  const [search, setSearch] = useState("");
  const [shuffling, setShuffling] = useState(false);
  const player = usePlayer();
  const { notifyError } = useToast();

  const { data, error, loading, reload, setPage } = usePagedApi(
    (page) => api.tracks({ page, pageSize: PAGE_SIZE, sort, q: search || undefined }),
    [sort, search],
    "tracks",
  );

  // Очередь набирает сервер из всей библиотеки: на странице видна только сотня, а перемешать
  // пользователь просит фонотеку.
  const shuffle = async () => {
    setShuffling(true);
    try {
      const tracks = await api.shuffleTracks({ q: search || undefined });
      if (tracks.length === 0) return;

      if (!player.shuffle) player.toggleShuffle();
      player.playQueue(tracks, 0, { source: "tracks" });
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
        subtitle={data ? t("count.tracksInLibrary", { count: data.total }) : undefined}
        actions={
          data && data.total > 0 ? (
            <button type="button" className="button" onClick={shuffle} disabled={shuffling}>
              {shuffling ? t("action.shuffling") : t("action.shuffle")}
            </button>
          ) : undefined
        }
      />

      <div className="page-tools">
        <SearchField value={search} onChange={setSearch} placeholder={t("filter.tracks")} />

        <label className="select-field">
          <span className="sr-only">{t("sort.label")}</span>
          <select value={sort} onChange={(event) => setSort(event.target.value as TrackSort)}>
            {Object.entries(sortKeys).map(([value, key]) => (
              <option key={value} value={value}>
                {t(key)}
              </option>
            ))}
          </select>
        </label>
      </div>

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton variant="row" count={12} />}

      {data && (
        <>
          <TrackList
            tracks={data.items}
            onChanged={reload}
            origin={{ source: "tracks" }}
            emptyMessage={search ? t("filter.nothingMatched") : undefined}
          />

          <Pagination result={data} onChange={setPage} />
        </>
      )}
    </>
  );
}
