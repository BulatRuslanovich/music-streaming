"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { api, artistImageUrl } from "@/lib/api";
import { accentFor, initialsFor } from "@/lib/format";
import type { Artist } from "@/lib/types";
import { useToast } from "@/contexts/ToastContext";
import { ImageIcon, TrashIcon } from "./Icons";
import { Modal } from "./Modal";

const ACCEPTED_TYPES = "image/jpeg,image/png,image/webp";
const MAX_IMAGE_BYTES = 8 * 1024 * 1024;

export function EditArtistDialog({
  artist,
  onClose,
  onSaved,
}: {
  artist: Artist;
  onClose: () => void;
  onSaved?: () => void;
}) {
  const { notify, notifyError } = useToast();
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
      notify("That image is larger than 8 MB.", "error");
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

      notify("Artist updated.", "success");
      onSaved?.();
      onClose();
    } catch (reason) {
      notifyError(reason, "Could not save the artist.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal title="Edit artist" onClose={onClose}>
      <form className="modal-body" onSubmit={save}>
        <div className="image-picker">
          <div
            className="image-picker-preview"
            style={shown ? undefined : { background: accentFor(artist.name || "?") }}
          >
            {shown ? (
              <img src={shown} alt={`Photo of ${artist.name}`} />
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
              {shown ? "Replace photo" : "Choose photo"}
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
                Remove photo
              </button>
            )}

            <p className="hint">JPEG, PNG or WebP up to 8 MB. Cropped to a 640×640 square.</p>
          </div>
        </div>

        <label htmlFor="field-artist-name">Name</label>
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
            {saving ? "Saving…" : "Save changes"}
          </button>
          <button type="button" className="button" onClick={onClose} disabled={saving}>
            Cancel
          </button>
        </div>
      </form>
    </Modal>
  );
}
