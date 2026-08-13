"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { Modal } from "./Modal";

const MIN_PASSWORD_LENGTH = 8;

export function CreateUserDialog({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated?: () => void;
}) {
  const { notify, notifyError } = useToast();
  const t = useT();

  const [username, setUsername] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [isAdmin, setIsAdmin] = useState(false);
  const [saving, setSaving] = useState(false);

  const save = async (event: SubmitEvent) => {
    event.preventDefault();
    setSaving(true);

    try {
      const created = await api.createUser({
        username: username.trim().toLowerCase(),
        password,
        displayName: displayName.trim() || undefined,
        isAdmin,
      });

      notify(t("dialog.addUser.created", { username: created.username }), "success");
      onCreated?.();
      onClose();
    } catch (reason) {
      notifyError(reason, t("dialog.addUser.failed"));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal title={t("dialog.addUser.title")} onClose={onClose}>
      <form className="modal-body" onSubmit={save}>
        <label htmlFor="field-username">{t("field.username")}</label>
        <input
          id="field-username"
          type="text"
          value={username}
          maxLength={100}
          required
          autoFocus
          autoComplete="off"
          spellCheck={false}
          placeholder={t("dialog.addUser.usernameHint")}
          onChange={(event) => setUsername(event.target.value)}
        />

        <label htmlFor="field-display-name">{t("field.displayName")}</label>
        <input
          id="field-display-name"
          type="text"
          value={displayName}
          maxLength={100}
          placeholder={t("dialog.addUser.displayNameHint")}
          onChange={(event) => setDisplayName(event.target.value)}
        />

        <label htmlFor="field-password">{t("field.password")}</label>
        <input
          id="field-password"
          type="password"
          value={password}
          minLength={MIN_PASSWORD_LENGTH}
          maxLength={72}
          required
          autoComplete="new-password"
          onChange={(event) => setPassword(event.target.value)}
        />

        <label className="checkbox-field">
          <input
            type="checkbox"
            checked={isAdmin}
            onChange={(event) => setIsAdmin(event.target.checked)}
          />
          <span>{t("dialog.addUser.isAdmin")}</span>
        </label>

        <p className="hint">{t("dialog.addUser.passwordHint")}</p>

        <div className="modal-actions">
          <button type="submit" className="button button-primary" disabled={saving}>
            {saving ? t("action.creating") : t("dialog.addUser.submit")}
          </button>
          <button type="button" className="button" onClick={onClose} disabled={saving}>
            {t("action.cancel")}
          </button>
        </div>
      </form>
    </Modal>
  );
}
