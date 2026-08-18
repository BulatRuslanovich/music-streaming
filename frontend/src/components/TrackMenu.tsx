"use client";

import Link from "next/link";
import { ReactElement, useEffect, useState } from "react";
import { api } from "@/lib/api";
import { extensionOf } from "@/lib/audioFormats";
import { saveFile } from "@/lib/download";
import { recordEvent } from "@/lib/events";
import { formatArtists } from "@/lib/format";
import type { ArtistRef, Playlist, Track } from "@/lib/types";
import { useAuth } from "@/contexts/AuthContext";
import { useOffline } from "@/contexts/OfflineContext";
import { useT } from "@/contexts/I18nContext";
import { usePlayer } from "@/contexts/PlayerContext";
import { useToast } from "@/contexts/ToastContext";
import { EditArtistDialog, type EditableArtist } from "./EditArtistDialog";
import { EditTrackDialog } from "./EditTrackDialog";
import { TrackInfoDialog } from "./TrackInfoDialog";
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
  AlbumIcon,
  ArtistIcon,
  DownloadIcon,
  EditIcon,
  HeartIcon,
  InfoIcon,
  MoreIcon,
  OfflineIcon,
  PlayNextIcon,
  PlusIcon,
  QueueIcon,
  RadioIcon,
  ShareIcon,
  TrashIcon,
} from "./Icons";

const RADIO_LENGTH = 30;

export function TrackMenu({
  track,
  open,
  onOpenChange,
  playlistId,
  onChanged,
  onQueue,
  isFavorite,
  onToggleFavorite,
  loadPlaylists,
  onNavigate,
  trigger,
}: {
  track: Track;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  playlistId?: string;
  onChanged?: () => void;
  onQueue: () => void;
  isFavorite?: boolean;
  onToggleFavorite?: () => void;
  loadPlaylists: () => Promise<Playlist[]>;
  onNavigate?: () => void;
  trigger?: ReactElement;
}) {
  const { notify, notifyError } = useToast();
  const offline = useOffline();
  const { isAdmin } = useAuth();
  const t = useT();
  const [playlists, setPlaylists] = useState<Playlist[] | null>(null);
  const [editing, setEditing] = useState(false);
  const [showingInfo, setShowingInfo] = useState(false);
  const [editingArtist, setEditingArtist] = useState<EditableArtist | null>(null);
  const [openingArtist, setOpeningArtist] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [startingRadio, setStartingRadio] = useState(false);
  const [confirm, confirmDialog] = useConfirm();
  const player = usePlayer();

  const credits: ArtistRef[] = track.artists?.length
    ? track.artists
    : [{ id: track.artistId, name: track.artistName }];

  useEffect(() => {
    if (!open || playlists !== null) return;

    let active = true;
    void loadPlaylists().then((result) => {
      if (active) setPlaylists(result);
    });

    return () => {
      active = false;
    };
  }, [open, playlists, loadPlaylists]);

  const addTo = async (playlist: Playlist) => {
    try {
      await api.addToPlaylist(playlist.id, track.id);
      recordEvent({ type: "trackAddedToPlaylist", trackId: track.id, entityId: playlist.id });
      notify(t("menu.addedToPlaylist", { name: playlist.name }), "success");
      onOpenChange(false);
    } catch (error) {
      notifyError(error, t("menu.addToPlaylistFailed"));
    }
  };

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

  const startRadio = async () => {
    setStartingRadio(true);

    try {
      const similar = await api.similarTracks(track.id, RADIO_LENGTH);
      const tracks = [track, ...similar.map((item) => item.track)];

      player.playQueue(tracks, 0, { source: "radio", sourceId: track.id });
      notify(t("menu.radioStarted", { title: track.title }), "success");
      onOpenChange(false);
    } catch (error) {
      notifyError(error, t("menu.radioFailed"));
    } finally {
      setStartingRadio(false);
    }
  };

  const editArtist = async (artist: ArtistRef) => {
    setOpeningArtist(true);

    try {
      const detail = await api.artist(artist.id, { page: 1, pageSize: 1 });
      setEditingArtist({ id: detail.id, name: detail.name, hasImage: detail.hasImage });
      onOpenChange(false);
    } catch (error) {
      notifyError(error, t("menu.editArtistFailed"));
    } finally {
      setOpeningArtist(false);
    }
  };

  const download = async () => {
    setDownloading(true);

    try {
      saveFile(
        await api.downloadTrack(
          track.id,
          `${track.title}${extensionOf(track.originalFileName) || ".mp3"}`,
        ),
      );
      onOpenChange(false);
    } catch (error) {
      notifyError(error, t("menu.downloadFailed"));
    } finally {
      setDownloading(false);
    }
  };

  const removeFromPlaylist = async () => {
    if (!playlistId) return;
    try {
      await api.removeFromPlaylist(playlistId, track.id);
      recordEvent({ type: "trackRemovedFromPlaylist", trackId: track.id, entityId: playlistId });
      notify(t("menu.removedFromPlaylist"), "success");
      onOpenChange(false);
      onChanged?.();
    } catch (error) {
      notifyError(error, t("menu.removeFromPlaylistFailed"));
    }
  };

  const deleteTrack = async () => {
    try {
      await api.deleteTrack(track.id);
      notify(t("menu.trackDeleted", { title: track.title }), "success");
      onOpenChange(false);
      onChanged?.();
    } catch (error) {
      notifyError(error, t("menu.deleteTrackFailed"));
    }
  };

  const offlineBusy = track.id in offline.progress;

  return (
    <>
      <DropdownMenu open={open} onOpenChange={onOpenChange}>
        <DropdownMenuTrigger asChild>
          {trigger ?? (
            <Button
              variant="ghost"
              size="icon"
              aria-label={t("tracks.moreActions", { title: track.title })}
            >
              <MoreIcon size={16} />
            </Button>
          )}
        </DropdownMenuTrigger>

        <DropdownMenuContent>
          {onToggleFavorite && (
            <DropdownMenuItem
              onAction={() => {
                onToggleFavorite();
                onOpenChange(false);
              }}
            >
              <HeartIcon size={16} filled={isFavorite} />{" "}
              {isFavorite ? t("menu.unlike") : t("menu.like")}
            </DropdownMenuItem>
          )}

          <DropdownMenuItem onAction={playNext}>
            <PlayNextIcon size={16} /> {t("menu.playNext")}
          </DropdownMenuItem>

          <DropdownMenuItem
            onAction={() => {
              onQueue();
              onOpenChange(false);
            }}
          >
            <QueueIcon size={16} /> {t("menu.addToQueue")}
          </DropdownMenuItem>

          <DropdownMenuItem disabled={startingRadio} onAction={() => void startRadio()}>
            <RadioIcon size={16} /> {startingRadio ? t("menu.radioStarting") : t("menu.radio")}
          </DropdownMenuItem>

          <DropdownMenuItem onAction={() => void share()}>
            <ShareIcon size={16} /> {t("menu.share")}
          </DropdownMenuItem>

          {track.albumId && (
            <DropdownMenuItem asChild>
              <Link href={`/albums/${track.albumId}`} onClick={onNavigate}>
                <AlbumIcon size={16} /> {t("menu.goToAlbum")}
              </Link>
            </DropdownMenuItem>
          )}

          {credits.map((artist) => (
            <DropdownMenuItem key={`go-${artist.id}`} asChild>
              <Link href={`/artists/${artist.id}`} onClick={onNavigate}>
                <ArtistIcon size={16} />{" "}
                {credits.length > 1
                  ? t("menu.goToArtistNamed", { name: artist.name })
                  : t("menu.goToArtist")}
              </Link>
            </DropdownMenuItem>
          ))}

          <DropdownMenuItem disabled={downloading} onAction={() => void download()}>
            <DownloadIcon size={16} /> {downloading ? t("menu.downloading") : t("menu.download")}
          </DropdownMenuItem>

          {offline.supported && (
            <DropdownMenuItem
              disabled={offlineBusy}
              onAction={() => {
                if (offline.has(track.id)) void offline.remove(track.id);
                else void offline.download(track);
              }}
            >
              <OfflineIcon size={16} />{" "}
              {offlineBusy
                ? t("offline.downloading")
                : offline.has(track.id)
                  ? t("offline.remove")
                  : t("offline.download")}
            </DropdownMenuItem>
          )}

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
              <EditIcon size={16} /> {t("menu.editDetails")}
            </DropdownMenuItem>
          )}

          {isAdmin &&
            credits.map((artist) => (
              <DropdownMenuItem
                key={artist.id}
                disabled={openingArtist}
                onAction={() => void editArtist(artist)}
              >
                <ArtistIcon size={16} />{" "}
                {credits.length > 1
                  ? t("menu.editArtistNamed", { name: artist.name })
                  : t("menu.editArtist")}
              </DropdownMenuItem>
            ))}

          <DropdownMenuSeparator />
          <DropdownMenuLabel>{t("menu.addToPlaylist")}</DropdownMenuLabel>

          {playlists === null && (
            <p className="px-2.5 py-1.5 text-sm text-faint">{t("common.loading")}</p>
          )}
          {playlists?.length === 0 && (
            <p className="px-2.5 py-1.5 text-sm text-faint">{t("menu.noPlaylists")}</p>
          )}
          {playlists?.map((playlist) => (
            <DropdownMenuItem key={playlist.id} onAction={() => void addTo(playlist)}>
              <PlusIcon size={16} /> {playlist.name}
            </DropdownMenuItem>
          ))}

          {(playlistId || isAdmin) && <DropdownMenuSeparator />}

          {playlistId && (
            <DropdownMenuItem onAction={() => void removeFromPlaylist()}>
              <TrashIcon size={16} /> {t("menu.removeFromPlaylist")}
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
                  action: () => void deleteTrack(),
                })
              }
            >
              <TrashIcon size={16} /> {t("menu.deleteFromLibrary")}
            </DropdownMenuItem>
          )}
        </DropdownMenuContent>
      </DropdownMenu>

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
