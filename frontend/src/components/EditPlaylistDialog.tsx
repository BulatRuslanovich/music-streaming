// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { api } from "@/lib/api";
import { playlistCoverUrl } from "@/lib/media";
import { limits, playlistSchema, type PlaylistValues } from "@/lib/schemas";
import { useFormat } from "@/lib/useFormat";
import { useSettings } from "@/contexts/SettingsContext";
import type { Playlist, PlaylistDetail } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { FormDialog } from "./FormDialog";
import { ImagePicker, noImageChosen, type ImageChoice } from "./ImagePicker";
import { CheckboxField, TextField } from "./ui/form";
import { PlaylistIcon } from "./Icons";

export function EditPlaylistDialog({
  playlist,
  onClose,
  onSaved,
}: {
  playlist: Playlist | PlaylistDetail;
  onClose: () => void;
  onSaved?: () => void;
}) {
  const t = useT();
  const format = useFormat();
  const { maxImageUploadBytes } = useSettings();
  const [cover, setCover] = useState<ImageChoice>(noImageChosen);

  const form = useForm<PlaylistValues>({
    resolver: zodResolver(playlistSchema),
    defaultValues: {
      name: playlist.name,
      description: playlist.description ?? "",
      isPublic: playlist.isPublic,
    },
  });

  const saving = form.formState.isSubmitting;

  return (
    <FormDialog
      title={t("dialog.editPlaylist.title")}
      form={form}
      onClose={onClose}
      submitLabel={t("action.saveChanges")}
      pendingLabel={t("action.saving")}
      successMessage={t("dialog.editPlaylist.saved")}
      errorMessage={t("dialog.editPlaylist.failed")}
      onSubmit={async ({ name, description, isPublic }) => {
        const changed =
          name !== playlist.name ||
          description !== (playlist.description ?? "") ||
          isPublic !== playlist.isPublic;

        if (changed) {
          await api.updatePlaylist(playlist.id, name, description || null, isPublic);
        }

        if (cover.file) await api.uploadPlaylistCover(playlist.id, cover.file);
        else if (cover.removed && playlist.hasCover) await api.removePlaylistCover(playlist.id);

        onSaved?.();
      }}
    >
      <ImagePicker
        value={cover}
        onChange={setCover}
        currentUrl={playlistCoverUrl({
          playlistId: playlist.id,
          hasCover: playlist.hasCover,
          coverTrackId: playlist.coverTrackId,
        })}
        name={playlist.name}
        fallback={<PlaylistIcon size={34} />}
        disabled={saving}
        labels={{
          choose: t("dialog.editPlaylist.chooseCover"),
          replace: t("dialog.editPlaylist.replaceCover"),
          remove: t("dialog.editPlaylist.removeCover"),
          hint: t("dialog.editPlaylist.imageHint", { limit: format.bytes(maxImageUploadBytes) }),
          alt: t("dialog.editPlaylist.coverAlt", { name: playlist.name }),
        }}
      />

      <TextField
        label={t("playlists.name")}
        registration={form.register("name")}
        error={form.formState.errors.name && t("form.required")}
        maxLength={limits.playlistName}
      />

      <TextField
        label={t("playlists.description")}
        registration={form.register("description")}
        maxLength={limits.playlistDescription}
      />

      <CheckboxField
        control={form.control}
        name="isPublic"
        label={t("playlists.makePublic")}
        hint={t("playlists.makePublicHint")}
      />
    </FormDialog>
  );
}
