// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { Loading } from "./Loading";
import { MobileHeader, MobileNav } from "./MobileNav";
import { Player } from "./Player";
import { Sidebar } from "./Sidebar";

/**
 * Каркас приложения: сайдбар слева, контент, плеер во всю ширину снизу. На телефоне сайдбар
 * уступает место шапке и нижней панели, плеер встаёт над ней.
 */
export function AppShell({ children }: { children: ReactNode }) {
  const { user, loading } = useAuth();
  const t = useT();
  const pathname = usePathname();
  const router = useRouter();

  const isLoginPage = pathname === "/login";

  // proxy.ts решает на переходах по страницам, но не видит отказ /auth/me при живой
  // куке-подсказке и вход на уже открытой странице логина — их доводит клиент.
  useEffect(() => {
    if (loading) return;
    if (!user && !isLoginPage) router.replace("/login");
    if (user && isLoginPage) router.replace("/");
  }, [loading, user, isLoginPage, router]);

  if (isLoginPage) return <>{children}</>;

  if (loading) return <Loading size="l" label={t("common.loadingLibrary")} />;

  if (!user) return null;

  return (
    <div className="grid h-dvh grid-cols-[var(--sidebar-width)_minmax(0,1fr)] grid-rows-[minmax(0,1fr)_auto] [grid-template-areas:'sidebar_content''player_player'] max-md:grid-cols-1 max-md:grid-rows-[auto_minmax(0,1fr)_auto_auto] max-md:[grid-template-areas:'mobile-header''content''player''nav']">
      {/* Первое, до чего доходит Tab: иначе на каждой странице приходилось проходить весь
          сайдбар. Спозиционирована вне потока, чтобы не занять ячейку сетки каркаса. */}
      <a
        href="#content"
        className="sr-only focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:z-100 focus:rounded-md focus:bg-popover focus:px-4 focus:py-2 focus:text-sm focus:font-medium focus:shadow-pop"
      >
        {t("nav.skipToContent")}
      </a>

      <Sidebar />
      <MobileHeader />

      <main
        id="content"
        tabIndex={-1}
        className="relative overflow-y-auto overscroll-contain outline-none [grid-area:content]"
      >
        <div className="mx-auto flex min-h-full max-w-[90rem] flex-col gap-11 px-10 pt-8 pb-12 max-lg:px-6 max-md:gap-8 max-md:px-4 max-md:pt-5 max-md:pb-8">
          {children}
        </div>
      </main>

      <Player />
      <MobileNav />
    </div>
  );
}
