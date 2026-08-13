"use client";

import Image from "next/image";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { LocaleSwitcher } from "@/components/LocaleSwitcher";

export default function LoginPage() {
  const { signIn } = useAuth();
  const { notifyError } = useToast();
  const t = useT();
  const router = useRouter();

  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const onSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSubmitting(true);

    try {
      await signIn(username.trim(), password);
      router.replace("/");
    } catch (reason) {
      notifyError(reason, t("auth.failed"));
      setPassword("");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="login-page">
      <div className="login-locale">
        <LocaleSwitcher />
      </div>

      <form className="login-card" onSubmit={onSubmit}>
        <div className="login-brand">
          <Image className="brand-logo" src="/logo.png" alt="" width={72} height={72} priority />
          <h1>Caimack</h1>
          <p className="muted">{t("auth.tagline")}</p>
        </div>

        <label htmlFor="username">{t("field.username")}</label>
        <input
          id="username"
          name="username"
          type="text"
          autoComplete="username"
          autoCapitalize="none"
          spellCheck={false}
          required
          value={username}
          onChange={(event) => setUsername(event.target.value)}
          disabled={submitting}
        />

        <label htmlFor="password">{t("field.password")}</label>
        <input
          id="password"
          name="password"
          type="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          disabled={submitting}
        />

        <button type="submit" className="button button-primary" disabled={submitting}>
          {submitting ? t("auth.signingIn") : t("auth.signIn")}
        </button>
      </form>
    </div>
  );
}
