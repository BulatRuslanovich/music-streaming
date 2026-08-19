// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { queries } from "@/lib/queries";
import { usePage } from "@/lib/usePage";
import { PageHeader, SectionHeader } from "@/components/PageHeader";
import { Pagination } from "@/components/PageToolbar";
import { PlayAllButton } from "@/components/PlayAllButton";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { ToggleGroup, ToggleGroupButton } from "@/components/ui/tabs";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 100;

export default function GenresPage() {
  const t = useT();
  const [selected, setSelected] = useState<string | null>(null);
  const [page, setPage] = usePage([selected]);

  const genres = useQuery(queries.genres());
  const tracks = useQuery(queries.genreTracks(selected, { page, pageSize: PAGE_SIZE }));

  const selectedGenre = genres.data?.find((genre) => genre.id === selected) ?? null;

  return (
    <>
      <PageHeader
        title={t("nav.genres")}
        subtitle={genres.data ? t("count.genres", { count: genres.data.length }) : undefined}
        actions={
          tracks.data && tracks.data.items.length > 0 ? (
            <PlayAllButton tracks={tracks.data.items} />
          ) : undefined
        }
      />

      <Query result={genres} skeletonCount={8} empty={{ title: t("genres.empty") }}>
        {(list) => (
          <ToggleGroup variant="chip" aria-label={t("nav.genres")}>
            {list.map((genre) => (
              <ToggleGroupButton
                key={genre.id}
                variant="chip"
                active={selected === genre.id}
                onClick={() => setSelected(selected === genre.id ? null : genre.id)}
              >
                {genre.name}
                <span className="text-2xs text-faint tabular-nums">{genre.trackCount}</span>
              </ToggleGroupButton>
            ))}
          </ToggleGroup>
        )}
      </Query>

      {selectedGenre && (
        <section className="flex flex-col gap-3">
          <SectionHeader title={selectedGenre.name} />

          <Query result={tracks} skeleton="row" skeletonCount={6}>
            {(result) =>
              result === null ? null : (
                <>
                  <TrackList
                    tracks={result.items}
                    origin={{ source: "genre", sourceId: selectedGenre.id }}
                  />
                  <Pagination result={result} onChange={setPage} />
                </>
              )
            }
          </Query>
        </section>
      )}
    </>
  );
}
