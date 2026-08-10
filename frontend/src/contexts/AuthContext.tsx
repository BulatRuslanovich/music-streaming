"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { api, onSessionExpired } from "@/lib/api";
import type { User } from "@/lib/types";

interface AuthState {
  user: User | null;
  /** True until the initial session probe finishes, so pages can hold off rendering. */
  loading: boolean;
  signIn: (username: string, password: string) => Promise<void>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const router = useRouter();

  // The session lives in HttpOnly cookies, so the only way to know whether one exists is to ask.
  useEffect(() => {
    let cancelled = false;

    api
      .me()
      .then((me) => {
        if (!cancelled) setUser(me);
      })
      .catch(() => {
        if (!cancelled) setUser(null);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, []);

  // A refresh that fails anywhere in the app ends the session here, in one place.
  useEffect(
    () =>
      onSessionExpired(() => {
        setUser(null);
        router.replace("/login");
      }),
    [router],
  );

  const signIn = useCallback(async (username: string, password: string) => {
    setUser(await api.login(username, password));
  }, []);

  const signOut = useCallback(async () => {
    try {
      await api.logout();
    } finally {
      setUser(null);
      router.replace("/login");
    }
  }, [router]);

  const value = useMemo<AuthState>(
    () => ({ user, loading, signIn, signOut }),
    [user, loading, signIn, signOut],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const context = useContext(AuthContext);
  if (!context) throw new Error("useAuth must be used inside <AuthProvider>");
  return context;
}
