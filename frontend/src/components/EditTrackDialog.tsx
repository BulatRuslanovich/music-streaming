"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { formatArtists } from "@/lib/format";
import type { Track } from "@/lib/types";
import { usePlayer } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { Modal } from "./Modal";


export function EditTrackDialog({
  track,
  onClose,
  onSaved,
}: {
  track: Track;
  onClose: () => void;
  onSaved?: () => void;
}) {
  const { notify, notifyError } = useToast();
  const player = usePlayer();
  const t = useT();

  const [title, setTitle] = useState(track.title);
  const [artist, setArtist] = useState(formatArtists(track));
  const [album, setAlbum] = useState(track.albumTitle ?? "");
  const [genre, setGenre] = useState(track.genreName ?? "");
  const [year, setYear] = useState(track.year?.toString() ?? "");
  const [trackNumber, setTrackNumber] = useState(track.trackNumber?.toString() ?? "");
  const [discNumber, setDiscNumber] = useState(track.discNumber?.toString() ?? "");
  const [saving, setSaving] = useState(false);

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    setSaving(true);

    const toNumber = (value: string) => {
      const trimmed = value.trim();
      if (trimmed === "") return null;
      const parsed = Number.parseInt(trimmed, 10);
      return Number.isFinite(parsed) ? parsed : null;
    };

    try {
      const updated = await api.updateTrack(track.id, {
        title: title.trim() || track.title,
        artist: artist.trim() || undefined,
        album: album.trim(),
        genre: genre.trim(),
        year: toNumber(year),
        trackNumber: toNumber(trackNumber),
        discNumber: toNumber(discNumber),
      });

      // Приводим то, что лежит в очереди, в соответствие с новыми метаданными.
      player.patchTrack(track.id, updated);

      notify(t("dialog.editTrack.saved"), "success");
      onSaved?.();
      onClose();
    } catch (reason) {
      notifyError(reason, t("dialog.editTrack.failed"));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal title={t("dialog.editTrack.title")} onClose={onClose}>
      <form className="modal-body" onSubmit={save}>
          <label htmlFor="field-title">{t("field.title")}</label>
          <input
            id="field-title"
            type="text"
            value={title}
            maxLength={400}
            required
            autoFocus
            onChange={(event) => setTitle(event.target.value)}
          />

          <label htmlFor="field-artist">{t("field.artist")}</label>
          <input
            id="field-artist"
            type="text"
            value={artist}
            maxLength={300}
            onChange={(event) => setArtist(event.target.value)}
          />

          <label htmlFor="field-album">{t("field.album")}</label>
          <input
            id="field-album"
            type="text"
            value={album}
            maxLength={300}
            placeholder={t("dialog.editTrack.albumHint")}
            onChange={(event) => setAlbum(event.target.value)}
          />

          <label htmlFor="field-genre">{t("field.genre")}</label>
          <input
            id="field-genre"
            type="text"
            value={genre}
            maxLength={150}
            onChange={(event) => setGenre(event.target.value)}
          />

          <div className="field-row">
            <div>
              <label htmlFor="field-year">{t("field.year")}</label>
              <input
                id="field-year"
                type="number"
                min={1}
                max={2999}
                value={year}
                onChange={(event) => setYear(event.target.value)}
              />
            </div>
            <div>
              <label htmlFor="field-track">{t("field.trackNumber")}</label>
              <input
                id="field-track"
                type="number"
                min={0}
                value={trackNumber}
                onChange={(event) => setTrackNumber(event.target.value)}
              />
            </div>
            <div>
              <label htmlFor="field-disc">{t("field.discNumber")}</label>
              <input
                id="field-disc"
                type="number"
                min={0}
                value={discNumber}
                onChange={(event) => setDiscNumber(event.target.value)}
              />
            </div>
          </div>

          <p className="hint">
            {t("dialog.editTrack.originalFile", { fileName: track.originalFileName })}
          </p>

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
