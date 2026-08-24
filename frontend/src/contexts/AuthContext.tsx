// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import React, {
  createContext,
  useCallback,
  useEffect,
  useMemo,
  useState,
  useSyncExternalStore,
} from "react";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { useRequiredContext } from "@/lib/useRequiredContext";
import { onSessionExpired } from "@/lib/http";
import { dropQueryCache } from "@/lib/queryPersistence";
import { readSessionHint } from "@/lib/sessionHint";
import { clearStreamCache } from "@/lib/streamCache";
import type { User } from "@/lib/types";

interface AuthState {
  user: User | null;
  isAdmin: boolean;
  loading: boolean;
  signIn: (username: string, password: string) => Promise<void>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

let cachedHint: User | null = null;

function hintSnapshot(): User | null {
  cachedHint ??= readSessionHint();
  return cachedHint;
}

function serverHintSnapshot(): User | null {
  return null;
}

function subscribeToHint(): () => void {
  return () => {};
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const hint = useSyncExternalStore(subscribeToHint, hintSnapshot, serverHintSnapshot);

  const [resolved, setResolved] = useState<{ user: User | null } | null>(null);
  const router = useRouter();

  const user = resolved ? resolved.user : hint;
  const loading = resolved === null && hint === null;

  useEffect(() => {
    let cancelled = false;

    api
      .me()
      .then((me) => {
        if (!cancelled) setResolved({ user: me });
      })
      .catch(() => {
        if (!cancelled) setResolved({ user: null });
      });

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(
    () =>
      onSessionExpired(() => {
        setResolved({ user: null });
        router.replace("/login");
      }),
    [router],
  );

  const signIn = useCallback(async (username: string, password: string) => {
    setResolved({ user: await api.login(username, password) });
  }, []);

  const signOut = useCallback(async () => {
    try {
      await api.logout();
    } finally {
      await clearStreamCache().catch(() => {});
      dropQueryCache();
      cachedHint = null;

      setResolved({ user: null });
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
  return useRequiredContext(AuthContext, "useAuth", "AuthProvider");
}
