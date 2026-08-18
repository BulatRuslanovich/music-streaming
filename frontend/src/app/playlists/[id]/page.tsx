"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { playlistCoverUrl } from "@/lib/media";
import { queries } from "@/lib/queries";
import { useFormat } from "@/lib/useFormat";
import { useCoverColor } from "@/lib/useCoverColor";
import { useEntityOpened } from "@/lib/useEntityOpened";
import { useInvalidate } from "@/lib/useInvalidate";
import { useAuth } from "@/contexts/AuthContext";
import { useToast } from "@/contexts/ToastContext";
import { Cover } from "@/components/Cover";
import { DetailHeader } from "@/components/DetailHeader";
import { EditPlaylistDialog } from "@/components/EditPlaylistDialog";
import { PlayAllButton } from "@/components/PlayAllButton";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { useConfirm } from "@/components/ui/alert-dialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { EditIcon, PlaylistIcon, TrashIcon } from "@/components/Icons";
import { useT } from "@/contexts/I18nContext";

export default function PlaylistPage() {
  const t = useT();
  const format = useFormat();

  const id = useParams<{ id: string }>().id;
  const router = useRouter();
  const client = useQueryClient();
  const invalidate = useInvalidate();
  const { notify, notifyError } = useToast();
  const { user } = useAuth();
  const [confirm, confirmDialog] = useConfirm();

  const playlist = useQuery(queries.playlist(id));
  useEntityOpened("playlistOpened", id);

  const data = playlist.data;
  const tint = useCoverColor(
    data
      ? playlistCoverUrl({
          playlistId: data.id,
          hasCover: data.hasCover,
          coverTrackId: data.coverTrackId,
        })
      : null,
  );

  const [editing, setEditing] = useState(false);

  const remove = async () => {
    try {
      await api.deletePlaylist(id);
      notify(t("playlists.deleted"), "success");
      invalidate("playlists");
      router.push("/playlists");
    } catch (reason) {
      notifyError(reason, t("playlists.deleteFailed"));
    }
  };

  const reorder = async (trackIds: string[]) => {
    const key = queries.playlist(id).queryKey;

    client.setQueryData(key, (current) =>
      current
        ? {
            ...current,
            tracks: trackIds
              .map((trackId) => current.tracks.find((track) => track.id === trackId))
              .filter((track): track is NonNullable<typeof track> => Boolean(track)),
          }
        : current,
    );

    try {
      await api.reorderPlaylist(id, trackIds);
    } catch (reason) {
      notifyError(reason, t("playlists.reorderFailed"));
      void client.invalidateQueries({ queryKey: key });
    }
  };

  return (
    <Query result={playlist} skeleton="row">
      {(detail) => {
        const isOwner = user?.id === detail.ownerId;

        return (
          <>
            <DetailHeader
              kind={t("playlists.kind")}
              title={detail.name}
              tint={tint}
              description={detail.description || undefined}
              art={
                <Cover
                  playlistId={detail.id}
                  hasCover={detail.hasCover}
                  coverTrackId={detail.coverTrackId}
                  name={detail.name}
                  variant="full"
                  fallback={<PlaylistIcon size={48} />}
                  className="size-full rounded-none"
                />
              }
              facts={
                <>
                  {!isOwner && <span>{t("playlists.by", { name: detail.ownerName })} · </span>}
                  {t("count.tracks", { count: detail.tracks.length })}
                  {detail.durationSeconds > 0 && (
                    <span> · {format.totalDuration(detail.durationSeconds)}</span>
                  )}
                  {isOwner && detail.isPublic && (
                    <>
                      {" · "}
                      <Badge>{t("playlists.publicBadge")}</Badge>
                    </>
                  )}
                </>
              }
              actions={
                <>
                  <PlayAllButton tracks={detail.tracks} name={detail.name} />
                  {isOwner && (
                    <>
                      <Button onClick={() => setEditing(true)}>
                        <EditIcon size={16} /> {t("action.edit")}
                      </Button>
                      <Button
                        variant="destructive"
                        onClick={() =>
                          confirm({
                            title: t("playlists.confirmDelete", { name: detail.name }),
                            confirmLabel: t("action.delete"),
                            destructive: true,
                            action: () => void remove(),
                          })
                        }
                      >
                        <TrashIcon size={16} /> {t("action.delete")}
                      </Button>
                    </>
                  )}
                </>
              }
            />

            <TrackList
              tracks={detail.tracks}
              playlistId={isOwner ? id : undefined}
              onReorder={isOwner ? (trackIds) => void reorder(trackIds) : undefined}
              origin={{ source: "playlist", sourceId: id }}
            />

            {isOwner && detail.tracks.length > 1 && (
              <p className="text-sm text-muted-foreground">{t("playlists.dragToReorder")}</p>
            )}

            {editing && isOwner && (
              <EditPlaylistDialog
                playlist={detail}
                onClose={() => setEditing(false)}
                onSaved={() => invalidate("playlists")}
              />
            )}

            {confirmDialog}
          </>
        );
      }}
    </Query>
  );
}
