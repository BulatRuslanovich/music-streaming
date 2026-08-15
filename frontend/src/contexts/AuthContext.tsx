"use client";

import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { onSessionExpired } from "@/lib/http";
import { clearOffline } from "@/lib/offline";
import type { User } from "@/lib/types";

interface AuthState {
  user: User | null;
  isAdmin: boolean;
  loading: boolean;
  signIn: (username: string, password: string) => Promise<void>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const router = useRouter();

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
      // Скачанное уходит вместе с сессией: на общем устройстве чужая музыка в кэше — это чужая
      // музыка в кэше. Service worker чистит своё сам, по сообщению.
      await clearOffline().catch(() => {});
      navigator.serviceWorker?.controller?.postMessage({ type: "clear-offline" });

      setUser(null);
      router.replace("/login");
    }
  }, [router]);

  const value = useMemo<AuthState>(
    () => ({ user, isAdmin: user?.isAdmin ?? false, loading, signIn, signOut }),
    [user, loading, signIn, signOut],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const context = useContext(AuthContext);
  if (!context) throw new Error("useAuth must be used inside <AuthProvider>");
  return context;
}
