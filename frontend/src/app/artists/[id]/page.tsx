// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import dynamic from "next/dynamic";
import { useQuery } from "@tanstack/react-query";
import { useParams } from "next/navigation";
import { useState } from "react";
import { queries } from "@/lib/queries";
import { useEntityOpened } from "@/lib/useEntityOpened";
import { useInvalidate } from "@/lib/useInvalidate";
import { usePage } from "@/lib/usePage";
import { ArtistCover } from "@/components/Cover";
import { DetailHeader } from "@/components/DetailHeader";
import { AlbumCard } from "@/components/MediaCard";
import { CardGrid, SectionHeader } from "@/components/PageHeader";
import { Pagination } from "@/components/PageToolbar";
import { PlayAllButton } from "@/components/PlayAllButton";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { Button } from "@/components/ui/button";
import { EditIcon } from "@/components/Icons";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";

// Диалоги тянут react-hook-form + zod (~40 КБ gzip), а открываются по клику. Статический
// импорт клал эту пару в общий бандл, потому что точка входа живёт на каждой странице.
const EditArtistDialog = dynamic(() =>
  import("@/components/EditArtistDialog").then((m) => m.EditArtistDialog),
);

const PAGE_SIZE = 100;

export default function ArtistPage() {
  const t = useT();
  const { isAdmin } = useAuth();
  const invalidate = useInvalidate();

  const id = useParams<{ id: string }>().id;
  const [page, setPage] = usePage([id]);
  const [editing, setEditing] = useState(false);

  useEntityOpened("artistOpened", id);

  const artist = useQuery(queries.artist(id, { page, pageSize: PAGE_SIZE }));

  return (
    <Query result={artist} skeleton="row">
      {(detail) => (
        <>
          <DetailHeader
            kind={t("artists.kind")}
            title={detail.name}
            round
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

          {detail.albums.length > 0 && (
            <section className="flex flex-col gap-3">
              <SectionHeader title={t("nav.albums")} />
              <CardGrid>
                {detail.albums.map((album) => (
                  <AlbumCard key={album.id} album={album} />
                ))}
              </CardGrid>
            </section>
          )}

          <section className="flex flex-col gap-3">
            <SectionHeader title={t("nav.tracks")} />
            <TrackList
              tracks={detail.tracks.items}
              showArtist={false}
              origin={{ source: "artist", sourceId: detail.id }}
            />
            <Pagination result={detail.tracks} onChange={setPage} />
          </section>

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
