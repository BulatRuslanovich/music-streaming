"use client";

import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import { formatArtists } from "@/lib/format";
import type { Track } from "@/lib/types";
import { usePlayer } from "@/contexts/PlayerContext";
import { useToast } from "@/contexts/ToastContext";
import { CloseIcon } from "./Icons";

/**
 * Lets the user correct metadata that was missing or wrong in a file's ID3 tags — the spec's
 * requirement that upload never forces manual entry, but still allows fixing it afterwards.
 *
 * Changing the artist, album or genre re-homes the track onto those library rows server-side, and
 * any album or artist left empty as a result is cleaned up there too.
 */
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
  const dialogRef = useRef<HTMLDivElement | null>(null);

  const [title, setTitle] = useState(track.title);
  // The full credit line: saving it splits the value again, so editing keeps every performer.
  const [artist, setArtist] = useState(formatArtists(track));
  const [album, setAlbum] = useState(track.albumTitle ?? "");
  const [genre, setGenre] = useState(track.genreName ?? "");
  const [year, setYear] = useState(track.year?.toString() ?? "");
  const [trackNumber, setTrackNumber] = useState(track.trackNumber?.toString() ?? "");
  const [discNumber, setDiscNumber] = useState(track.discNumber?.toString() ?? "");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    setSaving(true);

    // An empty number field means "leave unset", which the API models as null.
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

      // Keep whatever is in the queue consistent with the new metadata.
      player.patchTrack(track.id, updated);

      notify("Track details updated.", "success");
      onSaved?.();
      onClose();
    } catch (reason) {
      notifyError(reason, "Could not save the track details.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <div
      className="modal-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <div className="modal" role="dialog" aria-modal="true" aria-labelledby="edit-track-title" ref={dialogRef}>
        <header className="modal-header">
          <h2 id="edit-track-title">Edit track details</h2>
          <button type="button" className="icon-button" onClick={onClose} aria-label="Close">
            <CloseIcon size={18} />
          </button>
        </header>

        <form className="modal-body" onSubmit={save}>
          <label htmlFor="field-title">Title</label>
          <input
            id="field-title"
            type="text"
            value={title}
            maxLength={400}
            required
            autoFocus
            onChange={(event) => setTitle(event.target.value)}
          />

          <label htmlFor="field-artist">Artist</label>
          <input
            id="field-artist"
            type="text"
            value={artist}
            maxLength={300}
            onChange={(event) => setArtist(event.target.value)}
          />

          <label htmlFor="field-album">Album</label>
          <input
            id="field-album"
            type="text"
            value={album}
            maxLength={300}
            placeholder="Leave empty for a single"
            onChange={(event) => setAlbum(event.target.value)}
          />

          <label htmlFor="field-genre">Genre</label>
          <input
            id="field-genre"
            type="text"
            value={genre}
            maxLength={150}
            onChange={(event) => setGenre(event.target.value)}
          />

          <div className="field-row">
            <div>
              <label htmlFor="field-year">Year</label>
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
              <label htmlFor="field-track">Track no.</label>
              <input
                id="field-track"
                type="number"
                min={0}
                value={trackNumber}
                onChange={(event) => setTrackNumber(event.target.value)}
              />
            </div>
            <div>
              <label htmlFor="field-disc">Disc no.</label>
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
            Original file: {track.originalFileName}
          </p>

          <div className="modal-actions">
            <button type="submit" className="button button-primary" disabled={saving}>
              {saving ? "Saving…" : "Save changes"}
            </button>
            <button type="button" className="button" onClick={onClose} disabled={saving}>
              Cancel
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
