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
import { userAfterMeFailure } from "@/lib/authBootstrap";
import { useRequiredContext } from "@/lib/useRequiredContext";
import { onSessionExpired } from "@/lib/http";
import { dropQueryCache } from "@/lib/queryPersistence";
import { readSessionHint } from "@/lib/sessionHint";
import { cacheAppShell, clearStreamCache } from "@/lib/streamCache";
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

function subscribeToHint(): () => void {
  return () => {};
}

export function AuthProvider({
  children,
  initialUser = null,
}: {
  children: React.ReactNode;
  // Расшифрованная на сервере кука-подсказка. Раньше серверный снимок был всегда null, поэтому
  // в статическом HTML любого роута лежал спиннер «Loading your library», а настоящий каркас
  // появлялся только после гидратации и ответа /auth/me.
  initialUser?: User | null;
}) {
  const hint = useSyncExternalStore(subscribeToHint, hintSnapshot, () => initialUser);

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
        if (!cancelled) {
          setResolved({ user: userAfterMeFailure(hint, navigator.onLine) });
        }
      });

    return () => {
      cancelled = true;
    };
  }, [hint]);

  useEffect(
    () =>
      onSessionExpired(() => {
        setResolved({ user: null });
        router.replace("/login");
      }),
    [router],
  );

  useEffect(() => {
    if (user) void cacheAppShell();
  }, [user]);

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
