"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { api } from "@/lib/api";
import { limits, playlistSchema, type PlaylistValues } from "@/lib/schemas";
import { useT } from "@/contexts/I18nContext";
import { FormDialog } from "./FormDialog";
import { CheckboxField, TextField } from "./ui/form";

export function CreatePlaylistDialog({
  onClose,
  onCreated,
  afterCreate,
}: {
  onClose: () => void;
  onCreated?: () => void;
  afterCreate?: (playlistId: string) => Promise<void>;
}) {
  const t = useT();

  const form = useForm<PlaylistValues>({
    resolver: zodResolver(playlistSchema),
    defaultValues: { name: "", description: "", isPublic: false },
  });

  return (
    <FormDialog
      title={t("playlists.new")}
      form={form}
      onClose={onClose}
      submitLabel={t("action.create")}
      pendingLabel={t("action.creating")}
      errorMessage={t("playlists.createFailed")}
      onSubmit={async ({ name, description, isPublic }) => {
        const playlist = await api.createPlaylist(name, description || undefined, isPublic);
        await afterCreate?.(playlist.id);
        onCreated?.();
      }}
    >
      <TextField
        label={t("playlists.name")}
        registration={form.register("name")}
        error={form.formState.errors.name && t("form.required")}
        maxLength={limits.playlistName}
        autoFocus
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
