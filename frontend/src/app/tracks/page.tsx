"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { api, type TrackSort } from "@/lib/api";
import type { TranslationKey } from "@/lib/i18n";
import { queries } from "@/lib/queries";
import { usePage } from "@/lib/usePage";
import { useT } from "@/contexts/I18nContext";
import { usePlayer } from "@/contexts/PlayerContext";
import { useToast } from "@/contexts/ToastContext";
import { PageHeader } from "@/components/PageHeader";
import { Pagination, PageToolbar, SortSelect } from "@/components/PageToolbar";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { Button } from "@/components/ui/button";

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
  const { notifyError } = useToast();

  const [sort, setSort] = useState<TrackSort>("Title");
  const [search, setSearch] = useState("");
  const [shuffling, setShuffling] = useState(false);
  const [page, setPage] = usePage([sort, search]);

  const tracks = useQuery(
    queries.tracks({ page, pageSize: PAGE_SIZE, sort, q: search || undefined }),
  );

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
      />

      <Query result={tracks} skeleton="row" skeletonCount={12}>
        {(data) => (
          <>
            <TrackList
              tracks={data.items}
              origin={{ source: "tracks" }}
              emptyMessage={search ? t("filter.nothingMatched") : undefined}
            />

            <Pagination result={data} onChange={setPage} />
          </>
        )}
      </Query>
    </>
  );
}
