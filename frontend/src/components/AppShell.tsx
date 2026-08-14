"use client";

import * as Dialog from "@radix-ui/react-dialog";
import { motion, useReducedMotion } from "motion/react";
import Image from "next/image";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { ReactNode, useCallback, useEffect, useState } from "react";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { DURATION, EASE } from "@/lib/motion";
import { useKonamiCode } from "@/lib/useKonamiCode";
import { useSearchShortcut, useSearchShortcutLabel } from "@/lib/useSearchShortcut";
import { BuildBadge } from "./BuildBadge";
import { EasterEgg } from "./EasterEgg";
import { LocaleSwitcher } from "./LocaleSwitcher";
import { Player } from "./Player";
import {
  AlbumIcon,
  ArtistIcon,
  ClockIcon,
  HeartIcon,
  LibraryIcon,
  MoreIcon,
  NoteIcon,
  PlaylistIcon,
  SearchIcon,
  ShieldIcon,
  SignOutIcon,
  UploadIcon,
} from "./Icons";

const primaryNav = [
  { href: "/tracks", labelKey: "nav.tracks", icon: NoteIcon },
  { href: "/albums", labelKey: "nav.albums", icon: AlbumIcon },
  { href: "/artists", labelKey: "nav.artists", icon: ArtistIcon },
  { href: "/genres", labelKey: "nav.genres", icon: LibraryIcon },
] as const;

const searchNav = { href: "/search", labelKey: "nav.search", icon: SearchIcon } as const;

const libraryNav = [
  { href: "/favorites", labelKey: "nav.favorites", icon: HeartIcon },
  { href: "/playlists", labelKey: "nav.playlists", icon: PlaylistIcon },
  { href: "/recently-played", labelKey: "nav.recentlyPlayed", icon: ClockIcon },
  { href: "/upload", labelKey: "nav.upload", icon: UploadIcon },
] as const;

const adminNav = { href: "/admin", labelKey: "nav.admin", icon: ShieldIcon } as const;

const mobileNav = [
  { href: "/tracks", labelKey: "nav.tracks", icon: NoteIcon },
  { href: "/search", labelKey: "nav.search", icon: SearchIcon },
  { href: "/favorites", labelKey: "nav.favorites", icon: HeartIcon },
  { href: "/playlists", labelKey: "nav.playlists", icon: PlaylistIcon },
] as const;

const mobileSheetNav = [
  { href: "/albums", labelKey: "nav.albums", icon: AlbumIcon },
  { href: "/artists", labelKey: "nav.artists", icon: ArtistIcon },
  { href: "/genres", labelKey: "nav.genres", icon: LibraryIcon },
  { href: "/recently-played", labelKey: "nav.recentlyPlayed", icon: ClockIcon },
  { href: "/upload", labelKey: "nav.upload", icon: UploadIcon },
] as const;

function NavPill({ active, reduceMotion }: { active: boolean; reduceMotion: boolean | null }) {
  if (!active) return null;

  return (
    <motion.span
      className="nav-active-pill"
      layoutId="nav-active-pill"
      transition={reduceMotion ? { duration: 0 } : { duration: DURATION, ease: EASE }}
      aria-hidden="true"
    />
  );
}

export function AppShell({ children }: { children: ReactNode }) {
  const { user, isAdmin, loading, signOut } = useAuth();
  const t = useT();
  const pathname = usePathname();
  const router = useRouter();
  const shortcutLabel = useSearchShortcutLabel();
  const reduceMotion = useReducedMotion();

  useSearchShortcut();

  const isLoginPage = pathname === "/login";
  const [signingOut, setSigningOut] = useState(false);
  const [moreOpen, setMoreOpen] = useState(false);
  const [easterEggOpen, setEasterEggOpen] = useState(false);

  useKonamiCode(useCallback(() => setEasterEggOpen(true), []));

  useEffect(() => {
    if (!loading && !user && !isLoginPage) {
      router.replace("/login");
    }
  }, [loading, user, isLoginPage, router]);

  useEffect(() => {
    if (!loading && user && isLoginPage) {
      router.replace("/");
    }
  }, [loading, user, isLoginPage, router]);

  if (isLoginPage) {
    return <>{children}</>;
  }

  if (loading) {
    return (
      <div className="boot-screen">
        <div className="boot-equalizer" aria-hidden="true">
          <span />
          <span />
          <span />
          <span />
        </div>
        <p className="muted">{t("common.loadingLibrary")}</p>
      </div>
    );
  }

  if (!user) {
    return null;
  }

  const isActive = (href: string) =>
    href === "/" ? pathname === "/" : pathname === href || pathname.startsWith(`${href}/`);

  const libraryLinks = isAdmin ? [...libraryNav, adminNav] : libraryNav;

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          <Link href="/" aria-label={t("nav.home")} className="brand-link">
            <Image className="brand-logo" src="/logo.png" alt="" width={34} height={34} priority />
            <span className="brand-text">Caimack</span>
          </Link>
        </div>

        <nav aria-label={t("nav.browse")}>
          <Link
            href={searchNav.href}
            className={`nav-link ${isActive(searchNav.href) ? "is-active" : ""}`}
          >
            <NavPill active={isActive(searchNav.href)} reduceMotion={reduceMotion} />
            <searchNav.icon size={19} />
            <span>{t(searchNav.labelKey)}</span>
            <kbd className="nav-shortcut">{shortcutLabel}</kbd>
          </Link>

          {primaryNav.map(({ href, labelKey, icon: Icon }) => (
            <Link
              key={href}
              href={href}
              className={`nav-link ${isActive(href) ? "is-active" : ""}`}
            >
              <NavPill active={isActive(href)} reduceMotion={reduceMotion} />
              <Icon size={19} />
              <span>{t(labelKey)}</span>
            </Link>
          ))}
        </nav>

        <p className="nav-heading">{t("nav.library")}</p>
        <nav aria-label={t("nav.library")}>
          {libraryLinks.map(({ href, labelKey, icon: Icon }) => (
            <Link
              key={href}
              href={href}
              className={`nav-link ${isActive(href) ? "is-active" : ""}`}
            >
              <NavPill active={isActive(href)} reduceMotion={reduceMotion} />
              <Icon size={19} />
              <span>{t(labelKey)}</span>
            </Link>
          ))}
        </nav>

        <div className="sidebar-footer">
          <div className="footer-row">
            <span className="user-chip" title={user.username}>
              {user.displayName || user.username}
            </span>
            <LocaleSwitcher />
            <button
              type="button"
              className="icon-button"
              onClick={() => {
                setSigningOut(true);
                void signOut().finally(() => setSigningOut(false));
              }}
              disabled={signingOut}
              aria-label={t("nav.signOut")}
              title={t("nav.signOut")}
            >
              <SignOutIcon size={18} />
            </button>
          </div>

          <BuildBadge />
        </div>
      </aside>

      <main className="content">{children}</main>

      <Player />

      <nav className="mobile-nav" aria-label={t("nav.main")}>
        {mobileNav.map(({ href, labelKey, icon: Icon }) => (
          <Link
            key={href}
            href={href}
            className={`mobile-nav-link ${isActive(href) ? "is-active" : ""}`}
            aria-current={isActive(href) ? "page" : undefined}
          >
            <Icon size={20} />
            <span>{t(labelKey)}</span>
          </Link>
        ))}

        <button
          type="button"
          className={`mobile-nav-link ${moreOpen ? "is-active" : ""}`}
          onClick={() => setMoreOpen(true)}
          aria-expanded={moreOpen}
        >
          <MoreIcon size={20} />
          <span>{t("nav.more")}</span>
        </button>
      </nav>

      {moreOpen && (
        <MoreSheet
          onClose={() => setMoreOpen(false)}
          isActive={isActive}
          isAdmin={isAdmin}
          user={user.displayName || user.username}
          onSignOut={() => {
            setSigningOut(true);
            void signOut().finally(() => setSigningOut(false));
          }}
          signingOut={signingOut}
        />
      )}

      <EasterEgg open={easterEggOpen} onClose={() => setEasterEggOpen(false)} />
    </div>
  );
}

function MoreSheet({
  onClose,
  isActive,
  isAdmin,
  user,
  onSignOut,
  signingOut,
}: {
  onClose: () => void;
  isActive: (href: string) => boolean;
  isAdmin: boolean;
  user: string;
  onSignOut: () => void;
  signingOut: boolean;
}) {
  const t = useT();

  const links = isAdmin ? [...mobileSheetNav, adminNav] : mobileSheetNav;

  return (
    <Dialog.Root open onOpenChange={(open) => !open && onClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className="sheet-backdrop">
          <Dialog.Content className="sheet" aria-describedby={undefined}>
            <Dialog.Title asChild>
              <span className="sr-only">{t("nav.more")}</span>
            </Dialog.Title>
            <div className="sheet-grabber" aria-hidden="true" />

            <nav aria-label={t("nav.library")}>
              {links.map(({ href, labelKey, icon: Icon }) => (
                <Link
                  key={href}
                  href={href}
                  className={`nav-link ${isActive(href) ? "is-active" : ""}`}
                  onClick={onClose}
                >
                  <Icon size={19} />
                  <span>{t(labelKey)}</span>
                </Link>
              ))}
            </nav>

            <div className="sheet-footer">
              <div className="footer-row">
                <span className="user-chip" title={user}>
                  {user}
                </span>
                <LocaleSwitcher />
                <button
                  type="button"
                  className="icon-button"
                  onClick={onSignOut}
                  disabled={signingOut}
                  aria-label={t("nav.signOut")}
                >
                  <SignOutIcon size={18} />
                </button>
              </div>

              <BuildBadge />
            </div>
          </Dialog.Content>
        </Dialog.Overlay>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
