"use client";

import Link from "next/link";
import { api } from "@/lib/api";
import { formatBytes, formatTotalDuration } from "@/lib/format";
import { useApi } from "@/lib/useApi";
import { useAuth } from "@/contexts/AuthContext";
import {
  AlbumCard,
  EmptyState,
  LoadError,
  PageHeader,
  PlaylistCard,
  ShelfSection,
  Skeleton,
  TrackCards,
} from "@/components/ui";

export default function HomePage() {
  const { user } = useAuth();
  const { data, error, loading, reload } = useApi(() => api.home(12));

  if (loading && !data) {
    return (
      <>
        <PageHeader title="Home" />
        <Skeleton count={6} variant="shelf" />
      </>
    );
  }

  if (error) return <LoadError message={error} onRetry={reload} />;
  if (!data) return null;

  const { stats } = data;
  const libraryIsEmpty = stats.trackCount === 0;

  return (
    <>
      <PageHeader
        title={`Welcome back${user?.displayName ? `, ${user.displayName}` : ""}`}
        subtitle={
          libraryIsEmpty
            ? "Your library is empty."
            : `${stats.trackCount.toLocaleString()} tracks · ${stats.albumCount.toLocaleString()} albums · ` +
              `${stats.artistCount.toLocaleString()} artists · ${formatTotalDuration(stats.totalDurationSeconds)} · ` +
              formatBytes(stats.totalBytes)
        }
      />

      {libraryIsEmpty ? (
        <EmptyState
          title="Nothing here yet"
          description="Upload a few MP3 files and they will appear here."
          action={
            <Link href="/upload" className="button button-primary">
              Upload music
            </Link>
          }
        />
      ) : (
        <>
          {data.recentlyPlayed.length > 0 && (
            <ShelfSection title="Recently played" href="/recently-played">
              <TrackCards tracks={data.recentlyPlayed} context={data.recentlyPlayed} />
            </ShelfSection>
          )}

          <ShelfSection title="Recently added" href="/tracks">
            <TrackCards tracks={data.recentlyAdded} context={data.recentlyAdded} />
          </ShelfSection>

          {data.favorites.length > 0 && (
            <ShelfSection title="Favourites" href="/favorites">
              <TrackCards tracks={data.favorites} context={data.favorites} />
            </ShelfSection>
          )}

          {data.albums.length > 0 && (
            <ShelfSection title="Albums" href="/albums">
              {data.albums.map((album) => (
                <AlbumCard key={album.id} album={album} />
              ))}
            </ShelfSection>
          )}

          {data.playlists.length > 0 && (
            <ShelfSection title="Your playlists" href="/playlists">
              {data.playlists.map((playlist) => (
                <PlaylistCard key={playlist.id} playlist={playlist} />
              ))}
            </ShelfSection>
          )}
        </>
      )}
    </>
  );
}
