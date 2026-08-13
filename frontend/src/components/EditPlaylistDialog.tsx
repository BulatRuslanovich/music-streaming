"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { api } from "@/lib/api";
import { playlistCoverUrl } from "@/lib/media";
import { accentFor } from "@/lib/format";
import type { Playlist, PlaylistDetail } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { ImageIcon, PlaylistIcon, TrashIcon } from "./Icons";
import { Modal } from "./Modal";

const ACCEPTED_TYPES = "image/jpeg,image/png,image/webp";
const MAX_IMAGE_BYTES = 8 * 1024 * 1024;


export function EditPlaylistDialog({
  playlist,
  onClose,
  onSaved,
}: {
  playlist: Playlist | PlaylistDetail;
  onClose: () => void;
  onSaved?: () => void;
}) {
  const { notify, notifyError } = useToast();
  const t = useT();
  const fileInput = useRef<HTMLInputElement | null>(null);

  const [file, setFile] = useState<File | null>(null);
  const [name, setName] = useState(playlist.name);
  const [description, setDescription] = useState(playlist.description ?? "");
  const [isPublic, setIsPublic] = useState(playlist.isPublic);

  const [removeCover, setRemoveCover] = useState(false);
  const [saving, setSaving] = useState(false);

  const preview = useMemo(() => (file ? URL.createObjectURL(file) : null), [file]);

  useEffect(() => {
    if (!preview) return;
    return () => URL.revokeObjectURL(preview);
  }, [preview]);

  const choose = (chosen: File | null) => {
    if (!chosen) return;

    if (chosen.size > MAX_IMAGE_BYTES) {
      notify(t("dialog.editPlaylist.imageTooLarge"), "error");
      return;
    }

    setFile(chosen);
    setRemoveCover(false);
  };

  const fallbackCover = playlistCoverUrl({
    playlistId: playlist.id,
    hasCover: playlist.hasCover && !removeCover,
    coverTrackId: playlist.coverTrackId,
  });
  const shown = preview ?? fallbackCover;

  const save = async (event: SubmitEvent) => {
    event.preventDefault();
    setSaving(true);

    try {
      const trimmedName = name.trim();
      const trimmedDescription = description.trim();

      if (
        trimmedName !== playlist.name ||
        trimmedDescription !== (playlist.description ?? "") ||
        isPublic !== playlist.isPublic
      ) {
        await api.updatePlaylist(playlist.id, trimmedName, trimmedDescription || null, isPublic);
      }

      if (file) {
        await api.uploadPlaylistCover(playlist.id, file);
      } else if (removeCover && playlist.hasCover) {
        await api.removePlaylistCover(playlist.id);
      }

      notify(t("dialog.editPlaylist.saved"), "success");
      onSaved?.();
      onClose();
    } catch (reason) {
      notifyError(reason, t("dialog.editPlaylist.failed"));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal title={t("dialog.editPlaylist.title")} onClose={onClose}>
      <form className="modal-body" onSubmit={save}>
        <div className="image-picker">
          <div
            className="image-picker-preview image-picker-preview-square"
            style={shown ? undefined : { background: accentFor(playlist.name || "?") }}
          >
            {shown ? (
              <img src={shown} alt={t("dialog.editPlaylist.coverAlt", { name: playlist.name })} />
            ) : (
              <PlaylistIcon size={34} />
            )}
          </div>

          <div className="image-picker-actions">
            <input
              ref={fileInput}
              type="file"
              accept={ACCEPTED_TYPES}
              hidden
              onChange={(event) => choose(event.target.files?.[0] ?? null)}
            />
            <button
              type="button"
              className="button"
              onClick={() => fileInput.current?.click()}
              disabled={saving}
            >
              <ImageIcon size={16} />
              {playlist.hasCover && !removeCover
                ? t("dialog.editPlaylist.replaceCover")
                : t("dialog.editPlaylist.chooseCover")}
            </button>

            {(playlist.hasCover || file !== null) && !removeCover && (
              <button
                type="button"
                className="text-button is-danger"
                disabled={saving}
                onClick={() => {
                  setFile(null);
                  if (fileInput.current) fileInput.current.value = "";
                  setRemoveCover(playlist.hasCover);
                }}
              >
                <TrashIcon size={16} />
                {t("dialog.editPlaylist.removeCover")}
              </button>
            )}

            <p className="hint">{t("dialog.editPlaylist.imageHint")}</p>
          </div>
        </div>

        <label htmlFor="field-playlist-name">{t("playlists.name")}</label>
        <input
          id="field-playlist-name"
          type="text"
          value={name}
          maxLength={200}
          required
          onChange={(event) => setName(event.target.value)}
        />

        <label htmlFor="field-playlist-description">{t("playlists.description")}</label>
        <input
          id="field-playlist-description"
          type="text"
          value={description}
          maxLength={1000}
          onChange={(event) => setDescription(event.target.value)}
        />

        <label className="checkbox-field">
          <input
            type="checkbox"
            checked={isPublic}
            onChange={(event) => setIsPublic(event.target.checked)}
          />
          <span>{t("playlists.makePublic")}</span>
        </label>
        <p className="hint">{t("playlists.makePublicHint")}</p>

        <div className="modal-actions">
          <button type="submit" className="button button-primary" disabled={saving}>
            {saving ? t("action.saving") : t("action.saveChanges")}
          </button>
          <button type="button" className="button" onClick={onClose} disabled={saving}>
            {t("action.cancel")}
          </button>
        </div>
      </form>
    </Modal>
  );
}
