"use client";

import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import { saveFile } from "@/lib/download";
import type { Playlist, Track } from "@/lib/types";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { EditTrackDialog } from "./EditTrackDialog";
import { DownloadIcon, EditIcon, MoreIcon, PlusIcon, QueueIcon, TrashIcon } from "./Icons";

export function TrackMenu({
  track,
  open,
  onOpenChange,
  playlistId,
  onChanged,
  onQueue,
  loadPlaylists,
}: {
  track: Track;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  playlistId?: string;
  onChanged?: () => void;
  onQueue: () => void;
  loadPlaylists: () => Promise<Playlist[]>;
}) {
  const { notify, notifyError } = useToast();
  const { isAdmin } = useAuth();
  const t = useT();
  const [playlists, setPlaylists] = useState<Playlist[] | null>(null);
  const [editing, setEditing] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const containerRef = useRef<HTMLDivElement | null>(null);

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

  useEffect(() => {
    if (!open) return;

    const closeOnOutside = (event: MouseEvent) => {
      if (!containerRef.current?.contains(event.target as Node)) onOpenChange(false);
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") onOpenChange(false);
    };

    document.addEventListener("mousedown", closeOnOutside);
    document.addEventListener("keydown", closeOnEscape);

    return () => {
      document.removeEventListener("mousedown", closeOnOutside);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [open, onOpenChange]);

  const addTo = async (playlist: Playlist) => {
    try {
      await api.addToPlaylist(playlist.id, track.id);
      notify(t("menu.addedToPlaylist", { name: playlist.name }), "success");
      onOpenChange(false);
    } catch (error) {
      notifyError(error, t("menu.addToPlaylistFailed"));
    }
  };

  const download = async () => {
    setDownloading(true);

    try {
      saveFile(await api.downloadTrack(track.id, `${track.title}.mp3`));
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
      notify(t("menu.removedFromPlaylist"), "success");
      onOpenChange(false);
      onChanged?.();
    } catch (error) {
      notifyError(error, t("menu.removeFromPlaylistFailed"));
    }
  };

  const deleteTrack = async () => {
    const confirmed = window.confirm(t("menu.confirmDeleteTrack", { title: track.title }));
    if (!confirmed) return;

    try {
      await api.deleteTrack(track.id);
      notify(t("menu.trackDeleted", { title: track.title }), "success");
      onOpenChange(false);
      onChanged?.();
    } catch (error) {
      notifyError(error, t("menu.deleteTrackFailed"));
    }
  };

  return (
    <div className="menu-anchor" ref={containerRef}>
      <button
        type="button"
        className="icon-button"
        onClick={() => onOpenChange(!open)}
        aria-label={t("tracks.moreActions", { title: track.title })}
        aria-expanded={open}
      >
        <MoreIcon size={16} />
      </button>

      {open && (
        <div className="menu" role="menu">
          <button type="button" role="menuitem" onClick={onQueue}>
            <QueueIcon size={16} /> {t("menu.addToQueue")}
          </button>

          <button
            type="button"
            role="menuitem"
            onClick={() => void download()}
            disabled={downloading}
          >
            <DownloadIcon size={16} /> {downloading ? t("menu.downloading") : t("menu.download")}
          </button>

          {isAdmin && (
            <button
              type="button"
              role="menuitem"
              onClick={() => {
                setEditing(true);
                onOpenChange(false);
              }}
            >
              <EditIcon size={16} /> {t("menu.editDetails")}
            </button>
          )}

          <div className="menu-separator" />
          <p className="menu-label">{t("menu.addToPlaylist")}</p>

          {playlists === null && <span className="menu-hint">{t("common.loading")}</span>}
          {playlists?.length === 0 && <span className="menu-hint">{t("menu.noPlaylists")}</span>}
          {playlists?.map((playlist) => (
            <button
              key={playlist.id}
              type="button"
              role="menuitem"
              onClick={() => void addTo(playlist)}
            >
              <PlusIcon size={16} /> {playlist.name}
            </button>
          ))}

          {(playlistId || isAdmin) && <div className="menu-separator" />}

          {playlistId && (
            <button type="button" role="menuitem" onClick={() => void removeFromPlaylist()}>
              <TrashIcon size={16} /> {t("menu.removeFromPlaylist")}
            </button>
          )}

          {isAdmin && (
            <button type="button" role="menuitem" className="is-danger" onClick={() => void deleteTrack()}>
              <TrashIcon size={16} /> {t("menu.deleteFromLibrary")}
            </button>
          )}
        </div>
      )}

      {editing && (
        <EditTrackDialog track={track} onClose={() => setEditing(false)} onSaved={onChanged} />
      )}
    </div>
  );
}
