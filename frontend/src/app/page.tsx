"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { queries } from "@/lib/queries";
import { useFormat } from "@/lib/useFormat";
import { useAuth } from "@/contexts/AuthContext";
import { RecommendationShelves } from "@/components/RecommendationShelves";
import { AlbumCard, PlaylistCard, TrackCards } from "@/components/MediaCard";
import { PageHeader, Shelf } from "@/components/PageHeader";
import { Query } from "@/components/Query";
import { EmptyState } from "@/components/EmptyState";
import { Button } from "@/components/ui/button";
import { useT } from "@/contexts/I18nContext";

export default function HomePage() {
  const t = useT();
  const format = useFormat();

  const { user } = useAuth();
  const home = useQuery(queries.home());
  const recommendations = useQuery(queries.recommendations());

  const stats = home.data?.stats;
  const libraryIsEmpty = stats?.trackCount === 0;

  const summary = stats
    ? [
        t("count.tracks", { count: stats.trackCount }),
        t("count.albums", { count: stats.albumCount }),
        t("count.artists", { count: stats.artistCount }),
        format.totalDuration(stats.totalDurationSeconds),
        format.bytes(stats.totalBytes),
      ].join(" · ")
    : undefined;

  return (
    <>
      <PageHeader
        title={
          user?.displayName ? t("home.welcomeNamed", { name: user.displayName }) : t("home.welcome")
        }
        subtitle={libraryIsEmpty ? t("home.libraryEmpty") : summary}
      />

      <Query result={home} skeletonCount={6}>
        {(data) =>
          data.stats.trackCount === 0 ? (
            <EmptyState
              title={t("home.emptyTitle")}
              description={t("home.emptyDescription")}
              action={
                <Button variant="primary" asChild>
                  <Link href="/upload">{t("home.uploadMusic")}</Link>
                </Button>
              }
            />
          ) : (
            <>
              {recommendations.data && recommendations.data.sections.length > 0 && (
                <RecommendationShelves sections={recommendations.data.sections} />
              )}

              {data.recentlyPlayed.length > 0 && (
                <Shelf title={t("nav.recentlyPlayed")} href="/recently-played">
                  <TrackCards
                    tracks={data.recentlyPlayed}
                    context={data.recentlyPlayed}
                    origin={{ source: "home" }}
                  />
                </Shelf>
              )}

              <Shelf title={t("home.recentlyAdded")} href="/tracks">
                <TrackCards
                  tracks={data.recentlyAdded}
                  context={data.recentlyAdded}
                  origin={{ source: "home" }}
                />
              </Shelf>

              {data.favorites.length > 0 && (
                <Shelf title={t("nav.favorites")} href="/favorites">
                  <TrackCards
                    tracks={data.favorites}
                    context={data.favorites}
                    origin={{ source: "favorites" }}
                  />
                </Shelf>
              )}

              {data.albums.length > 0 && (
                <Shelf title={t("nav.albums")} href="/albums">
                  {data.albums.map((album) => (
                    <AlbumCard key={album.id} album={album} />
                  ))}
                </Shelf>
              )}

              {data.playlists.length > 0 && (
                <Shelf title={t("home.yourPlaylists")} href="/playlists">
                  {data.playlists.map((playlist) => (
                    <PlaylistCard key={playlist.id} playlist={playlist} />
                  ))}
                </Shelf>
              )}
            </>
          )
        }
      </Query>
    </>
  );
}
