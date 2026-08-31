// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { trackCoverUrl } from "@/lib/media";
import { TRACK_PAGE_SIZE } from "@/lib/pageSizes";
import { queries } from "@/lib/queries";
import { useCoverColor } from "@/lib/useCoverColor";
import { useFormat } from "@/lib/useFormat";
import { usePage } from "@/lib/usePage";
import { CoverMosaic } from "@/components/collection/CoverMosaic";
import { DetailHero } from "@/components/DetailHero";
import { Pagination } from "@/components/PageToolbar";
import { PlayAllButton } from "@/components/PlayAllButton";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { Button } from "@/components/ui/button";
import { HeartIcon } from "@/components/Icons";
import { useT } from "@/contexts/I18nContext";

export function FavoritesPage() {
  const t = useT();
  const format = useFormat();

  const [page, setPage] = usePage([]);
  const favorites = useQuery(queries.favorites({ page, pageSize: TRACK_PAGE_SIZE }));

  const data = favorites.data;
  const items = data?.items ?? [];

  const tint = useCoverColor(trackCoverUrl(items[0], "thumb"));

  const wholeListLoaded = data !== undefined && data.total <= items.length;
  const totalDuration = wholeListLoaded
    ? items.reduce((sum, track) => sum + track.durationSeconds, 0)
    : 0;

  return (
    <>
      <DetailHero
        kind={t("favorites.kind")}
        title={t("nav.favorites")}
        tint={tint}
        art={<CoverMosaic tracks={items} />}
        facts={
          data
            ? t("count.tracks", { count: data.total }) +
              (totalDuration > 0 ? ` · ${format.totalDuration(totalDuration)}` : "")
            : undefined
        }
        actions={items.length > 0 ? <PlayAllButton tracks={items} /> : undefined}
      />

      <Query
        result={favorites}
        skeleton="row"
        empty={{
          icon: <HeartIcon size={24} />,
          title: t("favorites.emptyTitle"),
          description: t("favorites.emptyDescription"),
          action: (
            <Button variant="primary" asChild>
              <Link href="/tracks">{t("favorites.browseTracks")}</Link>
            </Button>
          ),
        }}
      >
        {(result) => (
          <>
            <TrackList tracks={result.items} origin={{ source: "favorites" }} />
            <Pagination result={result} onChange={setPage} />
          </>
        )}
      </Query>
    </>
  );
}
