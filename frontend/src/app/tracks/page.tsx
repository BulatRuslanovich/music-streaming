"use client";

import { useState } from "react";
import { api, type TrackSort } from "@/lib/api";
import type { TranslationKey } from "@/lib/i18n";
import { useApi } from "@/lib/useApi";
import { useT } from "@/contexts/I18nContext";
import { usePlayer } from "@/contexts/PlayerContext";
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
  const [page, setPage] = useState(1);
  const player = usePlayer();

  const { data, error, loading, reload } = useApi(
    () => api.tracks({ page, pageSize: PAGE_SIZE, sort }),
    [page, sort],
  );

  return (
    <>
      <PageHeader
        title={t("nav.tracks")}
        subtitle={data ? t("count.tracksInLibrary", { count: data.total }) : undefined}
        actions={
          <>
            <label className="select-field">
              <span className="sr-only">{t("sort.label")}</span>
              <select
                value={sort}
                onChange={(event) => {
                  setSort(event.target.value as TrackSort);
                  setPage(1);
                }}
              >
                {Object.entries(sortKeys).map(([value, key]) => (
                  <option key={value} value={value}>
                    {t(key)}
                  </option>
                ))}
              </select>
            </label>

            {data && data.items.length > 0 && (
              <>
                <button
                  type="button"
                  className="button"
                  onClick={() => {
                    if (!player.shuffle) player.toggleShuffle();
                    player.playQueue(data.items, Math.floor(Math.random() * data.items.length));
                  }}
                >
                  {t("action.shuffle")}
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
