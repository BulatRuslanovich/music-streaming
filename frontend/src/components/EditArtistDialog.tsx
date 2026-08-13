"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { api } from "@/lib/api";
import { artistImageUrl } from "@/lib/media";
import { accentFor, initialsFor } from "@/lib/format";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { ImageIcon, TrashIcon } from "./Icons";
import { Modal } from "./Modal";

const ACCEPTED_TYPES = "image/jpeg,image/png,image/webp";
const MAX_IMAGE_BYTES = 8 * 1024 * 1024;

export interface EditableArtist {
  id: string;
  name: string;
  hasImage: boolean;
}

export function EditArtistDialog({
  artist,
  onClose,
  onSaved,
}: {
  artist: EditableArtist;
  onClose: () => void;
  onSaved?: () => void;
}) {
  const { notify, notifyError } = useToast();
  const t = useT();
  const fileInput = useRef<HTMLInputElement | null>(null);

  const [name, setName] = useState(artist.name);
  const [file, setFile] = useState<File | null>(null);
  const [removeImage, setRemoveImage] = useState(false);
  const [saving, setSaving] = useState(false);

  const preview = useMemo(() => (file ? URL.createObjectURL(file) : null), [file]);

  useEffect(() => {
    if (!preview) return;
    return () => URL.revokeObjectURL(preview);
  }, [preview]);

  const choose = (chosen: File | null) => {
    if (!chosen) return;

    if (chosen.size > MAX_IMAGE_BYTES) {
      notify(t("dialog.editArtist.imageTooLarge"), "error");
      return;
    }

    setFile(chosen);
    setRemoveImage(false);
  };

  const currentImage = artistImageUrl({ artistId: artist.id, hasImage: artist.hasImage });
  const shown = preview ?? (removeImage ? null : currentImage);
  const hasSomethingToRemove = artist.hasImage || file !== null;

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    setSaving(true);

    try {
      const trimmed = name.trim();
      if (trimmed && trimmed !== artist.name) {
        await api.updateArtist(artist.id, trimmed);
      }

      if (file) {
        await api.uploadArtistImage(artist.id, file);
      } else if (removeImage && artist.hasImage) {
        await api.removeArtistImage(artist.id);
      }

      notify(t("dialog.editArtist.saved"), "success");
      onSaved?.();
      onClose();
    } catch (reason) {
      notifyError(reason, t("dialog.editArtist.failed"));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal title={t("dialog.editArtist.title")} onClose={onClose}>
      <form className="modal-body" onSubmit={save}>
        <div className="image-picker">
          <div
            className="image-picker-preview"
            style={shown ? undefined : { background: accentFor(artist.name || "?") }}
          >
            {shown ? (
              <img src={shown} alt={t("dialog.editArtist.photoAlt", { name: artist.name })} />
            ) : (
              <span aria-hidden="true">{initialsFor(artist.name)}</span>
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
              {shown ? t("dialog.editArtist.replacePhoto") : t("dialog.editArtist.choosePhoto")}
            </button>

            {hasSomethingToRemove && !removeImage && (
              <button
                type="button"
                className="text-button is-danger"
                disabled={saving}
                onClick={() => {
                  setFile(null);
                  if (fileInput.current) fileInput.current.value = "";
                  setRemoveImage(artist.hasImage);
                }}
              >
                <TrashIcon size={16} />
                {t("dialog.editArtist.removePhoto")}
              </button>
            )}

            <p className="hint">{t("dialog.editArtist.imageHint")}</p>
          </div>
        </div>

        <label htmlFor="field-artist-name">{t("field.name")}</label>
        <input
          id="field-artist-name"
          type="text"
          value={name}
          maxLength={300}
          required
          onChange={(event) => setName(event.target.value)}
        />

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
