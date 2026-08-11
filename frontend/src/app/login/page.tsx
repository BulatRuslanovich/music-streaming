"use client";

import Image from "next/image";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useAuth } from "@/contexts/AuthContext";
import { useToast } from "@/contexts/ToastContext";

export default function LoginPage() {
  const { signIn } = useAuth();
  const { notifyError } = useToast();
  const router = useRouter();

  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const onSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    setSubmitting(true);

    try {
      await signIn(username.trim(), password);
      router.replace("/");
    } catch (reason) {
      notifyError(reason, "Sign-in failed.");
      setPassword("");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="login-page">
      <form className="login-card" onSubmit={onSubmit}>
        <div className="login-brand">
          <Image
            className="brand-logo"
            src="/logo.png"
            alt=""
            width={72}
            height={72}
            priority
          />
          <h1>CAIMACK</h1>
          <p className="muted">Sign in to reach your library.</p>
        </div>

        <label htmlFor="username">Username</label>
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

        <label htmlFor="password">Password</label>
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
          {submitting ? "Signing in…" : "Sign in"}
        </button>
      </form>
    </div>
  );
}
