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
import { formatArtists } from "@/lib/format";
import { queries } from "@/lib/queries";
import type { ArtistRef, Playlist, Track } from "@/lib/types";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { usePlayerActions } from "@/contexts/PlayerContext";
import { useToast } from "@/contexts/ToastContext";
import { Loading } from "./Loading";
import { useConfirm } from "./ui/alert-dialog";
import { Button } from "./ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "./ui/dropdown-menu";
import {
  CornerDownRightIcon,
  Disc3Icon,
  DownloadIcon,
  EllipsisVerticalIcon,
  HeartIcon,
  InfoIcon,
  ListVideoIcon,
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

interface TrackMenuProps {
  track: Track;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  playlistId?: string;
  playlistTrackIds?: string[];
  onChanged?: () => void;
  onQueue: () => void;
  isFavorite?: boolean;
  onToggleFavorite?: () => void;
  onNavigate?: () => void;
  trigger?: ReactElement;
}

/**
 * В списке треков такое меню приходится на каждую строку, а внутри у него полтора десятка
 * хуков, собственный AlertDialog и три ленивых диалога. Поэтому наружу вынесены только
 * триггер и Root: тело появляется у той строки, меню которой хоть раз открывали, и дальше
 * остаётся смонтированным — так и список монтируется дёшево, и анимация закрытия на месте,
 * и фокус не теряется на первом открытии.
 */
export function TrackMenu({ open, onOpenChange, trigger, ...rest }: TrackMenuProps) {
  const t = useT();
  const [everOpened, setEverOpened] = useState(open);

  // По пропу, а не в onOpenChange: меню открывают и снаружи (правый клик по строке), и тогда
  // Radix свой onOpenChange не зовёт — триггер становился «открытым», а тело не монтировалось.
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
  onQueue,
  isFavorite,
  onToggleFavorite,
  onNavigate,
}: Omit<TrackMenuProps, "open" | "trigger">) {
  const { notify, notifyError } = useToast();
  const { isAdmin } = useAuth();
  const t = useT();
  const [editing, setEditing] = useState(false);
  const [showingInfo, setShowingInfo] = useState(false);
  const [editingArtist, setEditingArtist] = useState<EditableArtist | null>(null);
  const [confirm, confirmDialog] = useConfirm();
  const player = usePlayerActions();
  // Тело монтируется на первом открытии — тогда же и запрос; соседние строки берут его из кэша.
  const playlists = useQuery(queries.playlists());

  const credits: ArtistRef[] = track.artists?.length
    ? track.artists
    : [{ id: track.artistId, name: track.artistName }];

  const addTo = useMutation({
    mutationFn: (playlist: Playlist) => api.addToPlaylist(playlist.id, [track.id]),
    onSuccess: (_, playlist) => {
      recordEvent({ type: "trackAddedToPlaylist", trackId: track.id, entityId: playlist.id });
      notify(t("menu.addedToPlaylist", { name: playlist.name }), "success");
      onOpenChange(false);
    },
  });

  const playNext = () => {
    player.playNext(track);
    notify(t("menu.playingNext", { title: track.title }), "success");
    onOpenChange(false);
  };

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

      onOpenChange(false);
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
    onSuccess: () => onChanged?.(),
  });

  const removeFromPlaylist = useMutation({
    mutationFn: (playlist: string) => api.removeFromPlaylist(playlist, track.id),
    onSuccess: (_, playlist) => {
      recordEvent({ type: "trackRemovedFromPlaylist", trackId: track.id, entityId: playlist });
      notify(t("menu.removedFromPlaylist"), "success", {
        label: t("action.undo"),
        run: () => undoRemove.mutate(playlist),
      });
      onOpenChange(false);
      onChanged?.();
    },
  });

  const deleteTrack = useMutation({
    mutationFn: () => api.deleteTrack(track.id),
    onSuccess: () => {
      notify(t("menu.trackDeleted", { title: track.title }), "success");
      onOpenChange(false);
      onChanged?.();
    },
  });

  return (
    <>
      <DropdownMenuContent>
        {onToggleFavorite && (
          <DropdownMenuItem
            onAction={() => {
              onToggleFavorite();
              onOpenChange(false);
            }}
          >
            <HeartIcon size={16} className={isFavorite ? "fill-current" : undefined} />{" "}
            {isFavorite ? t("menu.unlike") : t("menu.like")}
          </DropdownMenuItem>
        )}

        <DropdownMenuItem onAction={playNext}>
          <CornerDownRightIcon size={16} /> {t("menu.playNext")}
        </DropdownMenuItem>

        <DropdownMenuItem
          onAction={() => {
            onQueue();
            onOpenChange(false);
          }}
        >
          <ListVideoIcon size={16} /> {t("menu.addToQueue")}
        </DropdownMenuItem>

        <DropdownMenuItem disabled={radio.isPending} onAction={() => radio.mutate()}>
          <RadioIcon size={16} /> {radio.isPending ? t("menu.radioStarting") : t("menu.radio")}
        </DropdownMenuItem>

        <DropdownMenuItem onAction={() => void share()}>
          <Share2Icon size={16} /> {t("menu.share")}
        </DropdownMenuItem>

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

        <DropdownMenuItem disabled={download.isPending} onAction={() => download.mutate()}>
          <DownloadIcon size={16} />{" "}
          {download.isPending ? t("menu.downloading") : t("menu.download")}
        </DropdownMenuItem>

        <DropdownMenuItem
          onAction={() => {
            setShowingInfo(true);
            onOpenChange(false);
          }}
        >
          <InfoIcon size={16} /> {t("menu.trackInfo")}
        </DropdownMenuItem>

        {isAdmin && (
          <DropdownMenuItem
            onAction={() => {
              setEditing(true);
              onOpenChange(false);
            }}
          >
            <PencilIcon size={16} /> {t("menu.editDetails")}
          </DropdownMenuItem>
        )}

        {isAdmin &&
          credits.map((artist) => (
            <DropdownMenuItem
              key={artist.id}
              disabled={editArtist.isPending}
              onAction={() => editArtist.mutate(artist)}
            >
              <UsersRoundIcon size={16} />{" "}
              {credits.length > 1
                ? t("menu.editArtistNamed", { name: artist.name })
                : t("menu.editArtist")}
            </DropdownMenuItem>
          ))}

        <DropdownMenuSeparator />
        <DropdownMenuLabel>{t("menu.addToPlaylist")}</DropdownMenuLabel>

        {playlists.isPending && <Loading size="s" />}
        {playlists.data?.length === 0 && (
          <p className="px-2.5 py-1.5 text-sm text-faint">{t("menu.noPlaylists")}</p>
        )}
        {playlists.data?.map((playlist) => (
          <DropdownMenuItem key={playlist.id} onAction={() => addTo.mutate(playlist)}>
            <PlusIcon size={16} /> {playlist.name}
          </DropdownMenuItem>
        ))}

        {(playlistId || isAdmin) && <DropdownMenuSeparator />}

        {playlistId && (
          <DropdownMenuItem onAction={() => removeFromPlaylist.mutate(playlistId)}>
            <Trash2Icon size={16} /> {t("menu.removeFromPlaylist")}
          </DropdownMenuItem>
        )}

        {isAdmin && (
          <DropdownMenuItem
            variant="destructive"
            onAction={() =>
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
        )}
      </DropdownMenuContent>

      {confirmDialog}

      {editing && (
        <EditTrackDialog track={track} onClose={() => setEditing(false)} onSaved={onChanged} />
      )}

      {showingInfo && <TrackInfoDialog track={track} onClose={() => setShowingInfo(false)} />}

      {editingArtist && (
        <EditArtistDialog
          artist={editingArtist}
          onClose={() => setEditingArtist(null)}
          onSaved={onChanged}
        />
      )}
    </>
  );
}
