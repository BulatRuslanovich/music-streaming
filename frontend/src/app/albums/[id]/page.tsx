// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { coverUrl } from "@/lib/media";
import { queries } from "@/lib/queries";
import { useFormat } from "@/lib/useFormat";
import { useEntityOpened } from "@/lib/useEntityOpened";
import { useCoverColor } from "@/lib/useCoverColor";
import { Section } from "@/components/collection/Section";
import { AlbumCover } from "@/components/Cover";
import { DetailHeader } from "@/components/DetailHeader";
import { AlbumCard } from "@/components/MediaCard";
import { Shelf } from "@/components/PageHeader";
import { PlayAllButton } from "@/components/PlayAllButton";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { useT } from "@/contexts/I18nContext";

export default function AlbumPage() {
  const t = useT();
  const format = useFormat();

  const id = useParams<{ id: string }>().id;
  const album = useQuery(queries.album(id));

  useEntityOpened("albumOpened", id);

  const data = album.data;
  const tint = useCoverColor(data ? coverUrl({ albumId: data.id, hasCover: data.hasCover }) : null);

  const artistId = data?.artistId;
  const siblings = useQuery({
    ...queries.albums({ artistId, page: 1, pageSize: 12 }),
    enabled: artistId !== undefined,
  });

  // Сам альбом в полке «ещё у этого артиста» не нужен — он и так открыт.
  const more = (siblings.data?.items ?? []).filter((album) => album.id !== id);

  return (
    <Query result={album} skeleton="detail">
      {(detail) => (
        <>
          <DetailHeader
            kind={t("albums.kind")}
            title={detail.title}
            tint={tint}
            art={<AlbumCover album={detail} variant="full" className="size-full rounded-none" />}
            facts={
              <>
                <Link
                  href={`/artists/${detail.artistId}`}
                  className="font-semibold text-foreground"
                >
                  {detail.artistName}
                </Link>
                {detail.year ? <span> · {detail.year}</span> : null}
                <span> · {t("count.tracks", { count: detail.tracks.length })}</span>
                {detail.durationSeconds > 0 && (
                  <span> · {format.totalDuration(detail.durationSeconds)}</span>
                )}
              </>
            }
            actions={<PlayAllButton tracks={detail.tracks} name={detail.title} />}
          />

          <Section title={t("albums.tracks")}>
            <TrackList
              tracks={detail.tracks}
              showAlbum={false}
              showCover={false}
              showArtist
              useTrackNumbers
              origin={{ source: "album", sourceId: detail.id }}
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
        </>
      )}
    </Query>
  );
}
