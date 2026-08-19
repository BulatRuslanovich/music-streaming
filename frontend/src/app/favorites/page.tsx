// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { queries } from "@/lib/queries";
import { useFormat } from "@/lib/useFormat";
import { usePage } from "@/lib/usePage";
import { PageHeader } from "@/components/PageHeader";
import { Pagination } from "@/components/PageToolbar";
import { PlayAllButton } from "@/components/PlayAllButton";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { Button } from "@/components/ui/button";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 100;

export default function FavoritesPage() {
  const t = useT();
  const format = useFormat();

  const [page, setPage] = usePage([]);
  const favorites = useQuery(queries.favorites({ page, pageSize: PAGE_SIZE }));

  const data = favorites.data;
  const wholeListLoaded = data !== undefined && data.total <= data.items.length;
  const totalDuration = wholeListLoaded
    ? data.items.reduce((sum, track) => sum + track.durationSeconds, 0)
    : 0;

  return (
    <>
      <PageHeader
        title={t("nav.favorites")}
        subtitle={
          data
            ? t("count.tracks", { count: data.total }) +
              (totalDuration > 0 ? ` · ${format.totalDuration(totalDuration)}` : "")
            : undefined
        }
        actions={data && data.items.length > 0 ? <PlayAllButton tracks={data.items} /> : undefined}
      />

      <Query
        result={favorites}
        skeleton="row"
        empty={{
          title: t("favorites.emptyTitle"),
          description: t("favorites.emptyDescription"),
          action: (
            <Button variant="primary" asChild>
              <Link href="/tracks">{t("favorites.browseTracks")}</Link>
            </Button>
          ),
        }}
      >
        {(page) => (
          <>
            <TrackList tracks={page.items} origin={{ source: "favorites" }} />
            <Pagination result={page} onChange={setPage} />
          </>
        )}
      </Query>
    </>
  );
}
