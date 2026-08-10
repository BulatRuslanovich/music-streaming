"use client";

import { useSearchParams } from "next/navigation";
import { Suspense } from "react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { TrackList } from "@/components/TrackList";
import {
  AlbumCard,
  ArtistCard,
  LoadError,
  PageHeader,
  SectionHeader,
  Skeleton,
} from "@/components/ui";

export default function SearchPage() {
  // useSearchParams needs a Suspense boundary, since it makes the page depend on the URL.
  return (
    <Suspense fallback={<PageHeader title="Search" />}>
      <SearchView />
    </Suspense>
  );
}

/**
 * Results only: the input lives in the shell's search bar, which owns the debounce and writes the
 * query into the URL. This page renders whatever `?q=` says, which also makes results shareable.
 */
function SearchView() {
  const query = (useSearchParams().get("q") ?? "").trim();

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
      <PageHeader title="Search" />

      {!query && (
        <p className="empty-state">Start typing to search across your whole library.</p>
      )}

      {error && <LoadError message={error} onRetry={reload} />}
      {query && loading && !data && <Skeleton count={6} />}

      {query && isEmpty && !loading && (
        <p className="empty-state">Nothing matched “{query}”.</p>
      )}

      {data && !isEmpty && (
        <>
          {data.tracks.length > 0 && (
            <section>
              <SectionHeader title="Tracks" />
              <TrackList tracks={data.tracks} onChanged={reload} />
            </section>
          )}

          {data.albums.length > 0 && (
            <section>
              <SectionHeader title="Albums" />
              <div className="card-grid">
                {data.albums.map((album) => (
                  <AlbumCard key={album.id} album={album} />
                ))}
              </div>
            </section>
          )}

          {data.artists.length > 0 && (
            <section>
              <SectionHeader title="Artists" />
              <div className="card-grid">
                {data.artists.map((artist) => (
                  <ArtistCard key={artist.id} artist={artist} />
                ))}
              </div>
            </section>
          )}

          {data.genres.length > 0 && (
            <section>
              <SectionHeader title="Genres" />
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
