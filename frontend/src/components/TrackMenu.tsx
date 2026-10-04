// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import dynamic from "next/dynamic";
import type { EditableArtist } from "./EditArtistDialog";
import Link from "next/link";
import { useMutation, useQuery } from "@tanstack/react-query";
import { ReactElement, useState } from "react";
import { api } from "@/lib/api";
import { extensionOf } from "@/lib/playback/audioFormats";
import { saveFile } from "@/lib/download";
import { recordEvent } from "@/lib/events";
import { creditsOf, formatArtists } from "@/lib/format";
import { queries } from "@/lib/queries";
import { useInvalidate } from "@/lib/useInvalidate";
import type { ArtistRef, Playlist, Track } from "@/lib/types";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { usePlayerActions } from "@/contexts/PlayerContext";
import { useToast } from "@/lib/useToast";
import { Loading } from "./Loading";
import { useConfirm } from "./ui/alert-dialog";
import { Button } from "./ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
} from "./ui/dropdown-menu";
import {
  CornerDownRightIcon,
  Disc3Icon,
  DownloadIcon,
  EllipsisVerticalIcon,
  HeartIcon,
  InfoIcon,
  ListMusicIcon,
  ListPlusIcon,
  ListVideoIcon,
  ListXIcon,
  PencilIcon,
  PlusIcon,
  RadioIcon,
  Share2Icon,
  Trash2Icon,
  UsersRoundIcon,
} from "lucide-react";

const EditArtistDialog = dynamic(() =>
  import("./EditArtistDialog").then((m) => m.EditArtistDialog),
);
const EditTrackDialog = dynamic(() => import("./EditTrackDialog").then((m) => m.EditTrackDialog));
const TrackInfoDialog = dynamic(() => import("./TrackInfoDialog").then((m) => m.TrackInfoDialog));
const PlaylistDialog = dynamic(() => import("./PlaylistDialog").then((m) => m.PlaylistDialog));

interface TrackMenuProps {
  track: Track;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  playlistId?: string;
  playlistTrackIds?: string[];
  onChanged?: () => void;
  isFavorite?: boolean;
  onToggleFavorite?: () => void;
  onNavigate?: () => void;
  trigger?: ReactElement;
}

export function TrackMenu({ open, onOpenChange, trigger, ...rest }: TrackMenuProps) {
  const t = useT();
  const [everOpened, setEverOpened] = useState(open);

  if (open && !everOpened) setEverOpened(true);

  return (
    <DropdownMenu open={open} onOpenChange={onOpenChange}>
      <DropdownMenuTrigger asChild>
        {trigger ?? (
          <Button
            variant="ghost"
            size="icon"
            aria-label={t("tracks.moreActions", { title: rest.track.title })}
          >
            <EllipsisVerticalIcon size={16} />
          </Button>
        )}
      </DropdownMenuTrigger>

      {everOpened && <TrackMenuBody {...rest} onOpenChange={onOpenChange} />}
    </DropdownMenu>
  );
}

function TrackMenuBody({
  track,
  onOpenChange,
  playlistId,
  playlistTrackIds,
  onChanged,
  isFavorite,
  onToggleFavorite,
  onNavigate,
}: Omit<TrackMenuProps, "open" | "trigger">) {
  const { notify, notifyError } = useToast();
  const { isAdmin } = useAuth();
  const t = useT();
  const [editing, setEditing] = useState(false);
  const [showingInfo, setShowingInfo] = useState(false);
  const [creatingPlaylist, setCreatingPlaylist] = useState(false);
  const [editingArtist, setEditingArtist] = useState<EditableArtist | null>(null);
  const [confirm, confirmDialog] = useConfirm();
  const player = usePlayerActions();
  const invalidate = useInvalidate();
  const playlists = useQuery(queries.playlists());

  const credits = creditsOf(track);

  const changed = () => {
    invalidate("library", "playlists");
    onChanged?.();
  };

  const stayOpen = (run: () => void) => (event: Event) => {
    event.preventDefault();
    run();
  };

  const addTo = useMutation({
    mutationFn: (playlist: Playlist) => api.addToPlaylist(playlist.id, [track.id]),
    onSuccess: (_, playlist) => {
      recordEvent({ type: "trackAddedToPlaylist", trackId: track.id, entityId: playlist.id });
      notify(t("menu.addedToPlaylist", { name: playlist.name }), "success");
      onOpenChange(false);
    },
  });

  const share = async () => {
    const path = track.albumId ? `/albums/${track.albumId}` : `/artists/${track.artistId}`;
    const url = `${window.location.origin}${path}`;

    try {
      if (navigator.share) {
        await navigator.share({
          title: track.title,
          text: `${track.title} — ${formatArtists(track)}`,
          url,
        });
      } else {
        await navigator.clipboard.writeText(url);
        notify(t("menu.linkCopied"), "success");
      }
    } catch (error) {
      if (error instanceof DOMException && error.name === "AbortError") return;
      notifyError(error, t("menu.shareFailed"));
    }
  };

  const radio = useMutation({
    mutationFn: () => player.startRadio(track),
    onSuccess: (started) => {
      if (!started) return;
      notify(t("menu.radioStarted", { title: track.title }), "success");
      onOpenChange(false);
    },
  });

  const editArtist = useMutation({
    mutationFn: (artist: ArtistRef) => api.artist(artist.id, { page: 1, pageSize: 1 }),
    onSuccess: (detail) => {
      setEditingArtist({ id: detail.id, name: detail.name, hasImage: detail.hasImage });
      onOpenChange(false);
    },
  });

  const download = useMutation({
    mutationFn: () =>
      api.downloadTrack(track.id, `${track.title}${extensionOf(track.originalFileName) || ".mp3"}`),
    onSuccess: (file) => {
      saveFile(file);
      onOpenChange(false);
    },
  });

  const undoRemove = useMutation({
    mutationFn: async (playlist: string) => {
      await api.addToPlaylist(playlist, [track.id]);
      if (playlistTrackIds) await api.reorderPlaylist(playlist, playlistTrackIds);
    },
    onSuccess: changed,
  });

  const removeFromPlaylist = useMutation({
    mutationFn: (playlist: string) => api.removeFromPlaylist(playlist, track.id),
    onSuccess: (_, playlist) => {
      recordEvent({ type: "trackRemovedFromPlaylist", trackId: track.id, entityId: playlist });
      notify(t("menu.removedFromPlaylist"), "success", {
        label: t("action.undo"),
        run: () => undoRemove.mutate(playlist),
      });
      changed();
    },
  });

  const deleteTrack = useMutation({
    mutationFn: () => api.deleteTrack(track.id),
    onSuccess: () => {
      notify(t("menu.trackDeleted", { title: track.title }), "success");
      changed();
    },
  });

  return (
    <>
      <DropdownMenuContent>
        <DropdownMenuItem
          onSelect={() => {
            player.playNext(track);
            notify(t("menu.playingNext", { title: track.title }), "success");
          }}
        >
          <CornerDownRightIcon size={16} /> {t("menu.playNext")}
        </DropdownMenuItem>

        <DropdownMenuItem
          onSelect={() => {
            player.addToQueue(track);
            notify(t("menu.addedToQueue", { title: track.title }), "success");
          }}
        >
          <ListVideoIcon size={16} /> {t("menu.addToQueue")}
        </DropdownMenuItem>

        <DropdownMenuItem disabled={radio.isPending} onSelect={stayOpen(() => radio.mutate())}>
          <RadioIcon size={16} /> {radio.isPending ? t("menu.radioStarting") : t("menu.radio")}
        </DropdownMenuItem>

        <DropdownMenuSeparator />

        {onToggleFavorite && (
          <DropdownMenuItem onSelect={onToggleFavorite}>
            <HeartIcon size={16} className={isFavorite ? "fill-current" : undefined} />{" "}
            {isFavorite ? t("menu.unlike") : t("menu.like")}
          </DropdownMenuItem>
        )}

        <DropdownMenuSub>
          <DropdownMenuSubTrigger>
            <ListPlusIcon size={16} /> {t("menu.addToPlaylist")}
          </DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            <DropdownMenuItem onSelect={() => setCreatingPlaylist(true)}>
              <PlusIcon size={16} /> {t("playlists.new")}
            </DropdownMenuItem>

            {(playlists.isPending || (playlists.data?.length ?? 0) > 0) && (
              <DropdownMenuSeparator />
            )}
            {playlists.isPending && <Loading size="s" />}
            {playlists.data?.map((playlist) => (
              <DropdownMenuItem key={playlist.id} onSelect={stayOpen(() => addTo.mutate(playlist))}>
                <ListMusicIcon size={16} /> {playlist.name}
              </DropdownMenuItem>
            ))}
          </DropdownMenuSubContent>
        </DropdownMenuSub>

        {playlistId && (
          <DropdownMenuItem onSelect={() => removeFromPlaylist.mutate(playlistId)}>
            <ListXIcon size={16} /> {t("menu.removeFromPlaylist")}
          </DropdownMenuItem>
        )}

        <DropdownMenuItem
          disabled={download.isPending}
          onSelect={stayOpen(() => download.mutate())}
        >
          <DownloadIcon size={16} />{" "}
          {download.isPending ? t("menu.downloading") : t("menu.download")}
        </DropdownMenuItem>

        <DropdownMenuSeparator />

        {track.albumId && (
          <DropdownMenuItem asChild>
            <Link href={`/albums/${track.albumId}`} onClick={onNavigate}>
              <Disc3Icon size={16} /> {t("menu.goToAlbum")}
            </Link>
          </DropdownMenuItem>
        )}

        {credits.map((artist) => (
          <DropdownMenuItem key={`go-${artist.id}`} asChild>
            <Link href={`/artists/${artist.id}`} onClick={onNavigate}>
              <UsersRoundIcon size={16} />{" "}
              {credits.length > 1
                ? t("menu.goToArtistNamed", { name: artist.name })
                : t("menu.goToArtist")}
            </Link>
          </DropdownMenuItem>
        ))}

        <DropdownMenuItem onSelect={() => void share()}>
          <Share2Icon size={16} /> {t("menu.share")}
        </DropdownMenuItem>

        <DropdownMenuItem onSelect={() => setShowingInfo(true)}>
          <InfoIcon size={16} /> {t("menu.trackInfo")}
        </DropdownMenuItem>

        {isAdmin && (
          <>
            <DropdownMenuSeparator />

            <DropdownMenuSub>
              <DropdownMenuSubTrigger>
                <PencilIcon size={16} /> {t("menu.manage")}
              </DropdownMenuSubTrigger>
              <DropdownMenuSubContent>
                <DropdownMenuItem onSelect={() => setEditing(true)}>
                  <PencilIcon size={16} /> {t("menu.editDetails")}
                </DropdownMenuItem>

                {credits.map((artist) => (
                  <DropdownMenuItem
                    key={artist.id}
                    disabled={editArtist.isPending}
                    onSelect={stayOpen(() => editArtist.mutate(artist))}
                  >
                    <UsersRoundIcon size={16} />{" "}
                    {credits.length > 1
                      ? t("menu.editArtistNamed", { name: artist.name })
                      : t("menu.editArtist")}
                  </DropdownMenuItem>
                ))}

                <DropdownMenuSeparator />

                <DropdownMenuItem
                  variant="destructive"
                  onSelect={() =>
                    confirm({
                      title: t("menu.confirmDeleteTrack", { title: track.title }),
                      confirmLabel: t("action.delete"),
                      destructive: true,
                      action: () => deleteTrack.mutate(),
                    })
                  }
                >
                  <Trash2Icon size={16} /> {t("menu.deleteFromLibrary")}
                </DropdownMenuItem>
              </DropdownMenuSubContent>
            </DropdownMenuSub>
          </>
        )}
      </DropdownMenuContent>

      {confirmDialog}

      {editing && (
        <EditTrackDialog track={track} onClose={() => setEditing(false)} onSaved={changed} />
      )}

      {showingInfo && <TrackInfoDialog track={track} onClose={() => setShowingInfo(false)} />}

      {creatingPlaylist && (
        <PlaylistDialog
          onClose={() => setCreatingPlaylist(false)}
          onSaved={() => invalidate("playlists")}
          afterCreate={async (id) => {
            await api.addToPlaylist(id, [track.id]);
            recordEvent({ type: "trackAddedToPlaylist", trackId: track.id, entityId: id });
          }}
        />
      )}

      {editingArtist && (
        <EditArtistDialog
          artist={editingArtist}
          onClose={() => setEditingArtist(null)}
          onSaved={changed}
        />
      )}
    </>
  );
}
