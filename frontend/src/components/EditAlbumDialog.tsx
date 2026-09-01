// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { api } from "@/lib/api";
import { coverUrl } from "@/lib/media";
import { albumSchema, limits, type AlbumInput, type AlbumValues } from "@/lib/schemas";
import { useFormat } from "@/lib/useFormat";
import { useSettings } from "@/contexts/SettingsContext";
import { useT } from "@/contexts/I18nContext";
import { FormDialog } from "./FormDialog";
import { AlbumIcon } from "./Icons";
import { ImagePicker, noImageChosen, type ImageChoice } from "./ImagePicker";
import { TextField } from "./ui/form";

interface EditableAlbum {
  id: string;
  title: string;
  artistName: string;
  year?: number | null;
  hasCover: boolean;
}

export function EditAlbumDialog({
  album,
  onClose,
  onSaved,
}: {
  album: EditableAlbum;
  onClose: () => void;
  onSaved?: () => void;
}) {
  const t = useT();
  const format = useFormat();
  const { maxImageUploadBytes } = useSettings();
  const [cover, setCover] = useState<ImageChoice>(noImageChosen);

  const form = useForm<AlbumInput, unknown, AlbumValues>({
    resolver: zodResolver(albumSchema),
    defaultValues: {
      title: album.title,
      artist: album.artistName,
      year: album.year?.toString() ?? "",
    },
  });

  const saving = form.formState.isSubmitting;

  return (
    <FormDialog
      title={t("dialog.editAlbum.title")}
      form={form}
      onClose={onClose}
      submitLabel={t("action.saveChanges")}
      pendingLabel={t("action.saving")}
      successMessage={t("dialog.editAlbum.saved")}
      errorMessage={t("dialog.editAlbum.failed")}
      onSubmit={async (values) => {
        await api.updateAlbum(album.id, {
          title: values.title,
          artist: values.artist,
          year: values.year,
        });

        if (cover.file) await api.uploadAlbumCover(album.id, cover.file);
        else if (cover.removed && album.hasCover) await api.removeAlbumCover(album.id);

        onSaved?.();
      }}
    >
      <ImagePicker
        value={cover}
        onChange={setCover}
        currentUrl={coverUrl({ albumId: album.id, hasCover: album.hasCover })}
        name={album.title}
        fallback={<AlbumIcon size={32} aria-hidden="true" />}
        disabled={saving}
        labels={{
          choose: t("dialog.editAlbum.chooseCover"),
          replace: t("dialog.editAlbum.replaceCover"),
          remove: t("dialog.editAlbum.removeCover"),
          hint: t("dialog.editAlbum.imageHint", { limit: format.bytes(maxImageUploadBytes) }),
          alt: t("dialog.editAlbum.coverAlt", { name: album.title }),
        }}
      />

      <TextField
        label={t("field.title")}
        registration={form.register("title")}
        error={form.formState.errors.title && t("form.required")}
        maxLength={limits.albumTitle}
        autoFocus
      />

      <TextField
        label={t("field.artist")}
        registration={form.register("artist")}
        error={form.formState.errors.artist && t("form.required")}
        maxLength={limits.artistName}
      />

      <TextField
        label={t("field.year")}
        registration={form.register("year")}
        type="number"
        min={1500}
        max={2999}
      />
    </FormDialog>
  );
}
