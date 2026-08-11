"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useToast } from "@/contexts/ToastContext";
import { Modal } from "./Modal";

const MIN_PASSWORD_LENGTH = 8;

/** Accounts are created here only: there is no self-service sign-up. */
export function CreateUserDialog({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated?: () => void;
}) {
  const { notify, notifyError } = useToast();

  const [username, setUsername] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [isAdmin, setIsAdmin] = useState(false);
  const [saving, setSaving] = useState(false);

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    setSaving(true);

    try {
      const created = await api.createUser({
        username: username.trim().toLowerCase(),
        password,
        displayName: displayName.trim() || undefined,
        isAdmin,
      });

      notify(`User ${created.username} created.`, "success");
      onCreated?.();
      onClose();
    } catch (reason) {
      notifyError(reason, "Could not create the user.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal title="Add user" onClose={onClose}>
      <form className="modal-body" onSubmit={save}>
        <label htmlFor="field-username">Username</label>
        <input
          id="field-username"
          type="text"
          value={username}
          maxLength={100}
          required
          autoFocus
          autoComplete="off"
          spellCheck={false}
          placeholder="lower-case letters, digits, . - _"
          onChange={(event) => setUsername(event.target.value)}
        />

        <label htmlFor="field-display-name">Display name</label>
        <input
          id="field-display-name"
          type="text"
          value={displayName}
          maxLength={100}
          placeholder="Defaults to the username"
          onChange={(event) => setDisplayName(event.target.value)}
        />

        <label htmlFor="field-password">Password</label>
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
          <span>Administrator — can manage users and edit the library</span>
        </label>

        <p className="hint">
          The password is set once here; there is no way to read it back afterwards.
        </p>

        <div className="modal-actions">
          <button type="submit" className="button button-primary" disabled={saving}>
            {saving ? "Creating…" : "Create user"}
          </button>
          <button type="button" className="button" onClick={onClose} disabled={saving}>
            Cancel
          </button>
        </div>
      </form>
    </Modal>
  );
}
