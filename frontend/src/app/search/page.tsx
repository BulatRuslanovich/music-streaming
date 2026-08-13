"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback } from "react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { SearchField } from "@/components/SearchField";
import { TrackList } from "@/components/TrackList";
import {
  AlbumCard,
  ArtistCard,
  LoadError,
  PageHeader,
  SectionHeader,
  Skeleton,
} from "@/components/ui";
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

  const { data, error, loading, reload } = useApi(
    () => (query ? api.search(query, 25) : Promise.resolve(null)),
    [query],
  );

  const isEmpty =
    data !== null &&
    data !== undefined &&
    data.artists.length === 0 &&
    data.albums.length === 0 &&
    data.tracks.length === 0 &&
    data.genres.length === 0;

  return (
    <>
      <PageHeader title={t("nav.search")} />

      <div className="page-tools">
        <SearchField
          value={query}
          onChange={setQuery}
          placeholder={t("search.placeholder")}
          autoFocus
        />
      </div>

      {!query && (
        <p className="empty-state">{t("search.hint")}</p>
      )}

      {error && <LoadError message={error} onRetry={reload} />}
      {query && loading && !data && <Skeleton count={6} />}

      {query && isEmpty && !loading && (
        <p className="empty-state">{t("search.nothingFound")}</p>
      )}

      {data && !isEmpty && (
        <>
          {data.tracks.length > 0 && (
            <section>
              <SectionHeader title={t("nav.tracks")} />
              <TrackList tracks={data.tracks} onChanged={reload} origin={{ source: "search" }} />
            </section>
          )}

          {data.albums.length > 0 && (
            <section>
              <SectionHeader title={t("nav.albums")} />
              <div className="card-grid">
                {data.albums.map((album) => (
                  <AlbumCard key={album.id} album={album} />
                ))}
              </div>
            </section>
          )}

          {data.artists.length > 0 && (
            <section>
              <SectionHeader title={t("nav.artists")} />
              <div className="card-grid">
                {data.artists.map((artist) => (
                  <ArtistCard key={artist.id} artist={artist} />
                ))}
              </div>
            </section>
          )}

          {data.genres.length > 0 && (
            <section>
              <SectionHeader title={t("nav.genres")} />
              <div className="chip-row">
                {data.genres.map((genre) => (
                  <span key={genre.id} className="chip">
                    {genre.name}
                    <span className="chip-count">{genre.trackCount}</span>
                  </span>
                ))}
              </div>
            </section>
          )}
        </>
      )}
    </>
  );
}
