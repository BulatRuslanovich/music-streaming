"use client";

import Link from "next/link";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { useFormat } from "@/lib/useFormat";
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

import { useT } from "@/contexts/I18nContext";

export default function HomePage() {
  const t = useT();
  const format = useFormat();

  const { user } = useAuth();
  const { data, error, loading, reload } = useApi(() => api.home(12));

  if (loading && !data) {
    return (
      <>
        <PageHeader title={t("nav.home")} />
        <Skeleton count={6} variant="shelf" />
      </>
    );
  }

  if (error) return <LoadError message={error} onRetry={reload} />;
  if (!data) return null;

  const { stats } = data;
  const libraryIsEmpty = stats.trackCount === 0;

  const summary = [
    t("count.tracks", { count: stats.trackCount }),
    t("count.albums", { count: stats.albumCount }),
    t("count.artists", { count: stats.artistCount }),
    format.totalDuration(stats.totalDurationSeconds),
    format.bytes(stats.totalBytes),
  ].join(" · ");

  return (
    <>
      <PageHeader
        title={
          user?.displayName
            ? t("home.welcomeNamed", { name: user.displayName })
            : t("home.welcome")
        }
        subtitle={libraryIsEmpty ? t("home.libraryEmpty") : summary}
      />

      {libraryIsEmpty ? (
        <EmptyState
          title={t("home.emptyTitle")}
          description={t("home.emptyDescription")}
          action={
            <Link href="/upload" className="button button-primary">
              {t("home.uploadMusic")}
            </Link>
          }
        />
      ) : (
        <>
          {data.recentlyPlayed.length > 0 && (
            <ShelfSection title={t("nav.recentlyPlayed")} href="/recently-played">
              <TrackCards tracks={data.recentlyPlayed} context={data.recentlyPlayed} />
            </ShelfSection>
          )}

          <ShelfSection title={t("home.recentlyAdded")} href="/tracks">
            <TrackCards tracks={data.recentlyAdded} context={data.recentlyAdded} />
          </ShelfSection>

          {data.favorites.length > 0 && (
            <ShelfSection title={t("nav.favorites")} href="/favorites">
              <TrackCards tracks={data.favorites} context={data.favorites} />
            </ShelfSection>
          )}

          {data.albums.length > 0 && (
            <ShelfSection title={t("nav.albums")} href="/albums">
              {data.albums.map((album) => (
                <AlbumCard key={album.id} album={album} />
              ))}
            </ShelfSection>
          )}

          {data.playlists.length > 0 && (
            <ShelfSection title={t("home.yourPlaylists")} href="/playlists">
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
