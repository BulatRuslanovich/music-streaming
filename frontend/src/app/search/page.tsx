"use client";

import { useQuery } from "@tanstack/react-query";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback } from "react";
import { queries } from "@/lib/queries";
import { AlbumCard, ArtistCard } from "@/components/MediaCard";
import { CardGrid, PageHeader, SectionHeader } from "@/components/PageHeader";
import { PageToolbar } from "@/components/PageToolbar";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { EmptyState } from "@/components/EmptyState";
import { Badge } from "@/components/ui/badge";
import { useT } from "@/contexts/I18nContext";

export default function SearchPage() {
  const t = useT();

  return (
    <Suspense fallback={<PageHeader title={t("nav.search")} />}>
      <SearchView />
    </Suspense>
  );
}

function SearchView() {
  const t = useT();
  const router = useRouter();

  const query = (useSearchParams().get("q") ?? "").trim();

  const setQuery = useCallback(
    (next: string) => router.replace(next ? `/search?q=${encodeURIComponent(next)}` : "/search"),
    [router],
  );

  const results = useQuery(queries.search(query));

  return (
    <>
      <PageHeader title={t("nav.search")} />

      <PageToolbar search={query} onSearch={setQuery} placeholder={t("search.placeholder")} />

      {!query ? (
        <EmptyState title={t("search.hint")} />
      ) : (
        <Query
          result={results}
          skeletonCount={6}
          isEmpty={(data) =>
            data.artists.length === 0 &&
            data.albums.length === 0 &&
            data.tracks.length === 0 &&
            data.genres.length === 0
          }
          empty={{ title: t("search.nothingFound") }}
        >
          {(data) => (
            <>
              {data.tracks.length > 0 && (
                <section className="flex flex-col gap-3">
                  <SectionHeader title={t("nav.tracks")} />
                  <TrackList tracks={data.tracks} origin={{ source: "search" }} />
                </section>
              )}

              {data.albums.length > 0 && (
                <section className="flex flex-col gap-3">
                  <SectionHeader title={t("nav.albums")} />
                  <CardGrid>
                    {data.albums.map((album) => (
                      <AlbumCard key={album.id} album={album} />
                    ))}
                  </CardGrid>
                </section>
              )}

              {data.artists.length > 0 && (
                <section className="flex flex-col gap-3">
                  <SectionHeader title={t("nav.artists")} />
                  <CardGrid>
                    {data.artists.map((artist) => (
                      <ArtistCard key={artist.id} artist={artist} />
                    ))}
                  </CardGrid>
                </section>
              )}

              {data.genres.length > 0 && (
                <section className="flex flex-col gap-3">
                  <SectionHeader title={t("nav.genres")} />
                  <div className="flex flex-wrap gap-2.5">
                    {data.genres.map((genre) => (
                      <Badge
                        key={genre.id}
                        variant="outline"
                        className="gap-2 px-3.5 py-1.5 text-sm"
                      >
                        {genre.name}
                        <span className="text-2xs text-faint tabular-nums">{genre.trackCount}</span>
                      </Badge>
                    ))}
                  </div>
                </section>
              )}
            </>
          )}
        </Query>
      )}
    </>
  );
}
