// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import dynamic from "next/dynamic";
import { useQuery } from "@tanstack/react-query";
import { useParams } from "next/navigation";
import { useState } from "react";
import { artistImageUrl } from "@/lib/media";
import { queries } from "@/lib/queries";
import { useCoverColor } from "@/lib/useCoverColor";
import { useEntityOpened } from "@/lib/useEntityOpened";
import { useInvalidate } from "@/lib/useInvalidate";
import { usePage } from "@/lib/usePage";
import { RankedList } from "@/components/collection/RankedList";
import { Section } from "@/components/collection/Section";
import { ArtistCover } from "@/components/Cover";
import { DetailHeader } from "@/components/DetailHeader";
import { AlbumCard, ArtistCard } from "@/components/MediaCard";
import { CardGrid, Shelf } from "@/components/PageHeader";
import { Pagination } from "@/components/PageToolbar";
import { PlayAllButton } from "@/components/PlayAllButton";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { Button } from "@/components/ui/button";
import { EditIcon } from "@/components/Icons";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";

const EditArtistDialog = dynamic(() =>
  import("@/components/EditArtistDialog").then((m) => m.EditArtistDialog),
);

const PAGE_SIZE = 100;

const GRID_THRESHOLD = 6;

export default function ArtistPage() {
  const t = useT();
  const { isAdmin } = useAuth();
  const invalidate = useInvalidate();

  const id = useParams<{ id: string }>().id;
  const [page, setPage] = usePage([id]);
  const [editing, setEditing] = useState(false);

  useEntityOpened("artistOpened", id);

  const artist = useQuery(queries.artist(id, { page, pageSize: PAGE_SIZE }));
  const top = useQuery(queries.artistTopTracks(id));
  const similar = useQuery(queries.similarArtists(id));

  const hasImage = artist.data?.hasImage ?? false;
  const tint = useCoverColor(hasImage ? artistImageUrl({ artistId: id, hasImage }) : null);

  const topTracks = top.data ?? [];
  const similarArtists = similar.data ?? [];

  const showTop = topTracks.length > 0 && (artist.data?.tracks.total ?? 0) > topTracks.length;

  return (
    <Query result={artist} skeleton="detail">
      {(detail) => (
        <>
          <DetailHeader
            kind={t("artists.kind")}
            title={detail.name}
            round
            tint={tint}
            art={<ArtistCover artist={detail} className="size-full" />}
            facts={
              <>
                {t("count.albums", { count: detail.albums.length })} ·{" "}
                {t("count.tracks", { count: detail.tracks.total })}
              </>
            }
            actions={
              <>
                <PlayAllButton tracks={detail.tracks.items} name={detail.name} />
                {isAdmin && (
                  <Button onClick={() => setEditing(true)}>
                    <EditIcon size={16} /> {t("action.edit")}
                  </Button>
                )}
              </>
            }
          />

          {showTop && (
            <Section title={t("artists.topTracks")}>
              <RankedList tracks={topTracks} origin={{ source: "artist", sourceId: detail.id }} />
            </Section>
          )}

          {detail.albums.length > 0 &&
            (detail.albums.length < GRID_THRESHOLD ? (
              <Shelf title={t("artists.discography")}>
                {detail.albums.map((album) => (
                  <AlbumCard key={album.id} album={album} />
                ))}
              </Shelf>
            ) : (
              <Section title={t("artists.discography")}>
                <CardGrid>
                  {detail.albums.map((album) => (
                    <AlbumCard key={album.id} album={album} />
                  ))}
                </CardGrid>
              </Section>
            ))}

          <Section title={t("nav.tracks")}>
            <TrackList
              tracks={detail.tracks.items}
              showArtist={false}
              origin={{ source: "artist", sourceId: detail.id }}
            />
            <Pagination result={detail.tracks} onChange={setPage} />
          </Section>

          {similarArtists.length > 0 && (
            <Shelf title={t("artists.similar")}>
              {similarArtists.map((other) => (
                <ArtistCard key={other.id} artist={other} bare />
              ))}
            </Shelf>
          )}

          {editing && (
            <EditArtistDialog
              artist={{ id: detail.id, name: detail.name, hasImage: detail.hasImage }}
              onClose={() => setEditing(false)}
              onSaved={() => invalidate("library")}
            />
          )}
        </>
      )}
    </Query>
  );
}
