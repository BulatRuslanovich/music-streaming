// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { motion, useReducedMotion } from "motion/react";
import Image from "next/image";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { ReactNode, useCallback, useEffect, useState } from "react";
import { cn } from "@/lib/cn";
import { DURATION, EASE } from "@/lib/motion";
import { useKonamiCode } from "@/lib/useKonamiCode";
import { useSearchShortcutLabel } from "@/lib/useSearchShortcut";
import { useAuth } from "@/contexts/AuthContext";
import { useOffline } from "@/contexts/OfflineContext";
import { useUpload } from "@/contexts/UploadContext";
import { useT, type Translate } from "@/contexts/I18nContext";
import { adminNav, dailyNav, moreNav, type NavEntry } from "@/lib/navigation";
import { BuildBadge } from "./BuildBadge";
import { CommandPalette } from "./CommandPalette";
import { Copyright } from "./Copyright";
import { EasterEgg } from "./EasterEgg";
import { Player } from "./Player";
import { ShortcutsDialog } from "./ShortcutsDialog";
import { Button } from "./ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from "./ui/dropdown-menu";
import { Sheet, SheetContent, SheetTitle } from "./ui/sheet";
import { MoreIcon, OfflineIcon, SearchIcon, SignOutIcon } from "./Icons";

type Overlay = "palette" | "shortcuts" | null;

const navLinkClass =
  "relative flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium text-muted-foreground transition-colors duration-150 ease-brand hover:bg-accent hover:text-foreground hover:no-underline data-[active=true]:font-semibold data-[active=true]:text-primary";

function NavLink({
  entry,
  active,
  reduceMotion,
  onNavigate,
  children,
  t,
  pill = false,
}: {
  entry: NavEntry;
  active: boolean;
  reduceMotion: boolean | null;
  onNavigate?: () => void;
  children?: ReactNode;
  t: Translate;
  pill?: boolean;
}) {
  const Icon = entry.icon;

  return (
    <Link
      href={entry.href}
      data-active={active}
      aria-current={active ? "page" : undefined}
      onClick={onNavigate}
      className={cn(navLinkClass, active && !pill && "bg-primary-soft")}
    >
      {active && pill && (
        <motion.span
          layoutId="nav-active-pill"
          transition={reduceMotion ? { duration: 0 } : { duration: DURATION, ease: EASE }}
          className="absolute inset-0 z-0 rounded-lg bg-primary-soft"
          aria-hidden="true"
        />
      )}
      <span className="relative z-10 flex flex-1 items-center gap-3">
        <Icon size={19} />
        <span>{t(entry.labelKey)}</span>
        {children}
      </span>
    </Link>
  );
}

function AccountRow({
  user,
  onSignOut,
  signingOut,
  t,
}: {
  user: string;
  onSignOut: () => void;
  signingOut: boolean;
  t: Translate;
}) {
  return (
    <div className="flex items-center gap-2">
      <span className="min-w-0 flex-1 truncate rounded-lg bg-raised px-3 py-2 text-sm" title={user}>
        {user}
      </span>
      <Button
        variant="ghost"
        size="icon"
        onClick={onSignOut}
        disabled={signingOut}
        aria-label={t("nav.signOut")}
        title={t("nav.signOut")}
      >
        <SignOutIcon size={18} />
      </Button>
    </div>
  );
}

export function AppShell({ children }: { children: ReactNode }) {
  const { user, isAdmin, loading, signOut } = useAuth();
  const { isOffline } = useOffline();
  const { progress: uploadProgress } = useUpload();
  const t = useT();
  const pathname = usePathname();
  const router = useRouter();
  const shortcutLabel = useSearchShortcutLabel();
  const reduceMotion = useReducedMotion();

  const isLoginPage = pathname === "/login";
  const [signingOut, setSigningOut] = useState(false);
  const [moreOpen, setMoreOpen] = useState(false);
  const [easterEggOpen, setEasterEggOpen] = useState(false);
  const [overlay, setOverlay] = useState<Overlay>(null);

  useKonamiCode(useCallback(() => setEasterEggOpen(true), []));

  useEffect(() => {
    if (!loading && !user && !isLoginPage) router.replace("/login");
  }, [loading, user, isLoginPage, router]);

  useEffect(() => {
    if (!loading && user && isLoginPage) router.replace("/");
  }, [loading, user, isLoginPage, router]);

  const requestSignOut = useCallback(() => {
    setSigningOut(true);
    void signOut().finally(() => setSigningOut(false));
  }, [signOut]);

  if (isLoginPage) return <>{children}</>;

  if (loading) {
    return (
      <div className="grid h-dvh place-items-center content-center gap-4">
        <div className="flex h-10 items-end gap-1.5" aria-hidden="true">
          {[0, 1, 2, 3].map((bar) => (
            <span
              key={bar}
              className="w-1.5 animate-equalize rounded-full bg-primary"
              style={{ animationDelay: `${-0.9 + bar * 0.25}s` }}
            />
          ))}
        </div>
        <p className="text-muted-foreground">{t("common.loadingLibrary")}</p>
      </div>
    );
  }

  if (!user) return null;

  const isActive = (href: string) =>
    href === "/" ? pathname === "/" : pathname === href || pathname.startsWith(`${href}/`);

  const moreLinks = isAdmin ? [...moreNav, adminNav] : moreNav;
  const moreActive = moreLinks.some((entry) => isActive(entry.href));
  const account = user.displayName || user.username;

  const uploadBadge = (entry: NavEntry) =>
    entry.href === "/upload" &&
    uploadProgress !== null && (
      <span
        role="status"
        aria-label={t("upload.uploading", { progress: uploadProgress.percent })}
        className="ml-auto rounded-full bg-primary px-1.5 py-0.5 text-2xs font-semibold text-primary-foreground tabular-nums"
      >
        {uploadProgress.percent}%
      </span>
    );

  return (
    <div
      className={cn(
        "grid h-dvh gap-2 p-2",
        "grid-cols-[var(--sidebar-width)_1fr] grid-rows-[1fr_auto] [grid-template-areas:'sidebar_content''sidebar_player']",
        "max-md:grid-cols-1 max-md:grid-rows-[auto_minmax(0,1fr)_auto_auto] max-md:gap-0 max-md:p-0 max-md:[grid-template-areas:'mobile-header''content''player''nav']",
      )}
    >
      <aside className="flex flex-col gap-1 overflow-y-auto p-3 [grid-area:sidebar] max-md:hidden">
        <div className="mb-1 border-b border-border px-3 pt-2 pb-4">
          <Link
            href="/"
            aria-label={t("nav.home")}
            className="flex items-center gap-3 text-lg font-bold hover:no-underline"
          >
            <Image
              className="block size-9 rounded-[0.7rem] object-cover shadow-art"
              src="/logo.png"
              alt=""
              width={36}
              height={36}
              priority
            />
            <span>Caimack</span>
          </Link>
        </div>

        <nav aria-label={t("nav.main")} className="mt-5 flex flex-col gap-0.5">
          {dailyNav.map((entry) => (
            <NavLink
              key={entry.href}
              entry={entry}
              active={isActive(entry.href)}
              reduceMotion={reduceMotion}
              t={t}
              pill
            >
              {entry.href === "/search" && (
                <kbd className="ml-auto rounded-md border border-border px-1.5 text-2xs font-medium tracking-wide text-faint">
                  {shortcutLabel}
                </kbd>
              )}
            </NavLink>
          ))}

          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <button
                type="button"
                data-active={moreActive}
                className={cn(navLinkClass, "w-full", moreActive && "bg-primary-soft")}
              >
                <MoreIcon size={19} />
                <span>{t("nav.more")}</span>
                {uploadProgress !== null && (
                  <span className="ml-auto size-2 rounded-full bg-primary" aria-hidden="true" />
                )}
              </button>
            </DropdownMenuTrigger>
            <DropdownMenuContent side="right" align="end" className="ml-1 min-w-56">
              <DropdownMenuLabel>{t("nav.more")}</DropdownMenuLabel>
              {moreLinks.map((entry) => {
                const Icon = entry.icon;
                return (
                  <DropdownMenuItem key={entry.href} asChild>
                    <Link
                      href={entry.href}
                      aria-current={isActive(entry.href) ? "page" : undefined}
                      className={cn(
                        "hover:no-underline",
                        isActive(entry.href) && "bg-primary-soft text-primary",
                      )}
                    >
                      <Icon size={18} />
                      <span className="flex-1">{t(entry.labelKey)}</span>
                      {uploadBadge(entry)}
                    </Link>
                  </DropdownMenuItem>
                );
              })}
            </DropdownMenuContent>
          </DropdownMenu>
        </nav>

        <div className="mt-auto flex flex-col gap-2 border-t border-border pt-4">
          <AccountRow user={account} onSignOut={requestSignOut} signingOut={signingOut} t={t} />
          <BuildBadge />
          <Copyright />
        </div>
      </aside>

      <header
        className="hidden items-center justify-between border-b border-border bg-background px-4 [grid-area:mobile-header] max-md:flex"
        style={{
          minHeight: "calc(3.75rem + env(safe-area-inset-top))",
          paddingTop: "env(safe-area-inset-top)",
        }}
      >
        <Link
          href="/"
          aria-label={t("nav.home")}
          className="flex items-center gap-2.5 font-bold hover:no-underline"
        >
          <Image
            className="size-8 rounded-[0.65rem] object-cover shadow-art"
            src="/logo.png"
            alt=""
            width={32}
            height={32}
            priority
          />
          <span>Caimack</span>
        </Link>
        <Button variant="ghost" size="icon" asChild>
          <Link href="/search" aria-label={t("nav.search")}>
            <SearchIcon size={20} />
          </Link>
        </Button>
      </header>

      <main className="flex flex-col gap-8 overflow-y-auto overscroll-contain rounded-xl bg-background px-8 pt-7 pb-10 [grid-area:content] max-md:gap-7 max-md:rounded-none max-md:px-4 max-md:pt-5 max-md:pb-8">
        {children}
      </main>

      {isOffline && (
        <p
          role="status"
          className="fixed bottom-[calc(var(--player-height)+0.75rem)] left-1/2 z-40 flex -translate-x-1/2 items-center gap-2 rounded-full border border-border-strong bg-popover/90 px-3.5 py-2 text-sm shadow-pop backdrop-blur-md max-md:bottom-[calc(var(--player-height)+var(--mobile-nav-height)+env(safe-area-inset-bottom)+0.5rem)]"
        >
          <OfflineIcon size={16} />
          {t("offline.indicator")}
        </p>
      )}

      <Player onOverlay={setOverlay} />

      <nav
        aria-label={t("nav.main")}
        className="hidden grid-flow-col auto-cols-fr border-t border-border bg-card [grid-area:nav] max-md:grid"
        style={{
          height: "calc(var(--mobile-nav-height) + env(safe-area-inset-bottom))",
          paddingBottom: "env(safe-area-inset-bottom)",
        }}
      >
        {dailyNav.map(({ href, labelKey, icon: Icon }) => {
          const active = isActive(href);

          return (
            <Link
              key={href}
              href={href}
              aria-current={active ? "page" : undefined}
              className={cn(
                "flex min-w-0 flex-col items-center justify-center gap-0.5 px-0.5 text-[0.68rem] font-semibold hover:no-underline",
                active ? "text-primary" : "text-faint",
              )}
            >
              <Icon size={20} />
              <span className="max-w-full truncate">{t(labelKey)}</span>
            </Link>
          );
        })}

        <button
          type="button"
          onClick={() => setMoreOpen(true)}
          aria-expanded={moreOpen}
          className={cn(
            "flex min-w-0 flex-col items-center justify-center gap-0.5 px-0.5 text-[0.68rem] font-semibold",
            moreOpen || moreActive ? "text-primary" : "text-faint",
          )}
        >
          <span className="relative">
            <MoreIcon size={20} />
            {uploadProgress !== null && (
              <span
                className="absolute -top-0.5 -right-0.5 size-2 rounded-full bg-primary"
                aria-hidden="true"
              />
            )}
          </span>
          <span className="max-w-full truncate">{t("nav.more")}</span>
        </button>
      </nav>

      <Sheet open={moreOpen} onOpenChange={setMoreOpen}>
        <SheetContent>
          <SheetTitle className="sr-only">{t("nav.more")}</SheetTitle>

          <nav aria-label={t("nav.more")} className="flex flex-col gap-0.5">
            {moreLinks.map((entry) => (
              <NavLink
                key={entry.href}
                entry={entry}
                active={isActive(entry.href)}
                reduceMotion={reduceMotion}
                onNavigate={() => setMoreOpen(false)}
                t={t}
              >
                {uploadBadge(entry)}
              </NavLink>
            ))}
          </nav>

          <div className="mt-2 flex flex-col gap-2 border-t border-border pt-3">
            <AccountRow user={account} onSignOut={requestSignOut} signingOut={signingOut} t={t} />
            <BuildBadge />
            <Copyright />
          </div>
        </SheetContent>
      </Sheet>

      <EasterEgg open={easterEggOpen} onClose={() => setEasterEggOpen(false)} />

      {overlay === "palette" && (
        <CommandPalette
          onClose={() => setOverlay(null)}
          onOpenShortcuts={() => setOverlay("shortcuts")}
        />
      )}

      {overlay === "shortcuts" && <ShortcutsDialog onClose={() => setOverlay(null)} />}
    </div>
  );
}
