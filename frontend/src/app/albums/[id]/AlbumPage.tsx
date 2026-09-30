// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import dynamic from "next/dynamic";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { queries } from "@/lib/queries";
import { uniformAudioSpec } from "@/lib/format";
import { useFormat } from "@/lib/useFormat";
import { useEntityOpened } from "@/lib/useEntityOpened";
import { useInvalidate } from "@/lib/useInvalidate";
import { AlbumCover } from "@/components/Cover";
import { DetailHeader } from "@/components/DetailHeader";
import { PencilIcon } from "lucide-react";
import { AlbumCard } from "@/components/MediaCard";
import { Section } from "@/components/PageHeader";
import { Shelf } from "@/components/Shelf";
import { PlayAllButton } from "@/components/PlayAllButton";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";

const EditAlbumDialog = dynamic(() =>
  import("@/components/EditAlbumDialog").then((m) => m.EditAlbumDialog),
);

export function AlbumPage() {
  const t = useT();
  const format = useFormat();
  const { isAdmin } = useAuth();
  const invalidate = useInvalidate();

  const id = useParams<{ id: string }>().id;
  const [editing, setEditing] = useState(false);
  const album = useQuery(queries.album(id));

  useEntityOpened("albumOpened", id);

  const data = album.data;

  const artistId = data?.artistId;
  const siblings = useQuery({
    ...queries.albums({ artistId, page: 1, pageSize: 12 }),
    enabled: artistId !== undefined,
  });

  const more = (siblings.data?.items ?? []).filter((album) => album.id !== id);

  const tracks = data?.tracks ?? [];

  const hasFeatures = tracks.some((track) => track.artistId !== data?.artistId);
  const albumSpec = uniformAudioSpec(tracks);

  return (
    <Query result={album}>
      {(detail) => (
        <>
          <DetailHeader
            kind={t("albums.kind")}
            title={detail.title}
            art={
              <AlbumCover
                album={detail}
                variant="full"
                sizes="(min-width: 56.25rem) 280px, 128px"
              />
            }
            facts={[
              <Link
                key="artist"
                href={`/artists/${detail.artistId}`}
                className="font-medium text-foreground"
              >
                {detail.artistName}
              </Link>,
              detail.year,
              t("count.tracks", { count: detail.tracks.length }),
              detail.durationSeconds > 0 && format.totalDuration(detail.durationSeconds),
              albumSpec,
            ]}
            actions={
              <>
                <PlayAllButton tracks={detail.tracks} name={detail.title} />
                {isAdmin && (
                  <Button onClick={() => setEditing(true)}>
                    <PencilIcon size={16} /> {t("action.edit")}
                  </Button>
                )}
              </>
            }
          />

          <Section title={t("albums.tracks")}>
            <TrackList
              tracks={detail.tracks}
              showAlbum={false}
              showCover={false}
              showArtist={hasFeatures}
              showAudioSpec={albumSpec === null}
              useTrackNumbers
            />
          </Section>

          {more.length > 0 && (
            <Shelf
              title={t("albums.moreByArtist", { name: detail.artistName })}
              href={`/artists/${detail.artistId}`}
            >
              {more.map((album) => (
                <AlbumCard key={album.id} album={album} />
              ))}
            </Shelf>
          )}

          {editing && (
            <EditAlbumDialog
              album={{
                id: detail.id,
                title: detail.title,
                artistName: detail.artistName,
                year: detail.year,
                hasCover: detail.hasCover,
              }}
              onClose={() => setEditing(false)}
              onSaved={() => invalidate("library")}
            />
          )}
        </>
      )}
    </Query>
  );
}
