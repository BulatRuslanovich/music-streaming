"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { coverUrl } from "@/lib/media";
import { queries } from "@/lib/queries";
import { useFormat } from "@/lib/useFormat";
import { useEntityOpened } from "@/lib/useEntityOpened";
import { useCoverColor } from "@/lib/useCoverColor";
import { Cover } from "@/components/Cover";
import { DetailHeader } from "@/components/DetailHeader";
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

  return (
    <Query result={album} skeleton="row">
      {(detail) => (
        <>
          <DetailHeader
            kind={t("albums.kind")}
            title={detail.title}
            tint={tint}
            art={
              <Cover
                albumId={detail.id}
                hasCover={detail.hasCover}
                name={detail.title}
                variant="full"
                className="size-full rounded-none"
              />
            }
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

          <TrackList
            tracks={detail.tracks}
            showAlbum={false}
            showCover={false}
            showArtist
            useTrackNumbers
            origin={{ source: "album", sourceId: detail.id }}
          />
        </>
      )}
    </Query>
  );
}
