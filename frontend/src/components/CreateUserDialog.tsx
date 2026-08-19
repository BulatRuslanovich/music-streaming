// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { api } from "@/lib/api";
import { limits, newUserSchema, type NewUserValues } from "@/lib/schemas";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { FormDialog } from "./FormDialog";
import { CheckboxField, TextField } from "./ui/form";

export function CreateUserDialog({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated?: () => void;
}) {
  const t = useT();
  const { notify } = useToast();

  const form = useForm<NewUserValues>({
    resolver: zodResolver(newUserSchema),
    defaultValues: { username: "", displayName: "", password: "", isAdmin: false },
  });

  const errors = form.formState.errors;

  return (
    <FormDialog
      title={t("dialog.addUser.title")}
      form={form}
      onClose={onClose}
      submitLabel={t("dialog.addUser.submit")}
      pendingLabel={t("action.creating")}
      errorMessage={t("dialog.addUser.failed")}
      onSubmit={async ({ username, displayName, password, isAdmin }) => {
        const created = await api.createUser({
          username: username.toLowerCase(),
          password,
          displayName: displayName || undefined,
          isAdmin,
        });

        notify(t("dialog.addUser.created", { username: created.username }), "success");
        onCreated?.();
      }}
    >
      <TextField
        label={t("field.username")}
        registration={form.register("username")}
        error={errors.username && t("form.required")}
        maxLength={limits.username}
        placeholder={t("dialog.addUser.usernameHint")}
        autoComplete="off"
        spellCheck={false}
        autoFocus
      />

      <TextField
        label={t("field.displayName")}
        registration={form.register("displayName")}
        maxLength={limits.displayName}
        placeholder={t("dialog.addUser.displayNameHint")}
      />

      <TextField
        label={t("field.password")}
        type="password"
        registration={form.register("password")}
        error={errors.password && t("form.passwordShort", { count: limits.password.min })}
        hint={t("dialog.addUser.passwordHint")}
        autoComplete="new-password"
      />

      <CheckboxField control={form.control} name="isAdmin" label={t("dialog.addUser.isAdmin")} />
    </FormDialog>
  );
}
