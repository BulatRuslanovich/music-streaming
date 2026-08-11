"use client";

import Image from "next/image";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { Suspense, useEffect, useState } from "react";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { LocaleSwitcher } from "./LocaleSwitcher";
import { Player } from "./Player";
import { SearchBar } from "./SearchBar";
import {
  AlbumIcon,
  ArtistIcon,
  ClockIcon,
  HeartIcon,
  LibraryIcon,
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

const libraryNav = [
  { href: "/favorites", labelKey: "nav.favorites", icon: HeartIcon },
  { href: "/playlists", labelKey: "nav.playlists", icon: PlaylistIcon },
  { href: "/recently-played", labelKey: "nav.recentlyPlayed", icon: ClockIcon },
  { href: "/upload", labelKey: "nav.upload", icon: UploadIcon },
] as const;

const adminNav = { href: "/admin", labelKey: "nav.admin", icon: ShieldIcon } as const;

const mobileNav = [
  { href: "/search", labelKey: "nav.search", icon: SearchIcon },
  { href: "/playlists", labelKey: "nav.playlists", icon: PlaylistIcon },
  { href: "/favorites", labelKey: "nav.favorites", icon: HeartIcon },
  { href: "/upload", labelKey: "nav.upload", icon: UploadIcon },
] as const;


export function AppShell({ children }: { children: React.ReactNode }) {
  const { user, isAdmin, loading, signOut } = useAuth();
  const t = useT();
  const pathname = usePathname();
  const router = useRouter();

  const isLoginPage = pathname === "/login";
  const [signingOut, setSigningOut] = useState(false);

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
        <div className="boot-pulse" aria-hidden="true" />
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
          <Link href="/" aria-label={t("nav.home")}>
            <Image className="brand-logo" src="/logo.png" alt="" width={34} height={34} priority />
          </Link>

          <span className="brand-text">CAIMACK</span>
        </div>

        <nav aria-label={t("nav.browse")}>
          {primaryNav.map(({ href, labelKey, icon: Icon }) => (
            <Link key={href} href={href} className={`nav-link ${isActive(href) ? "is-active" : ""}`}>
              <Icon size={19} />
              <span>{t(labelKey)}</span>
            </Link>
          ))}
        </nav>

        <p className="nav-heading">{t("nav.library")}</p>
        <nav aria-label={t("nav.library")}>
          {libraryLinks.map(({ href, labelKey, icon: Icon }) => (
            <Link key={href} href={href} className={`nav-link ${isActive(href) ? "is-active" : ""}`}>
              <Icon size={19} />
              <span>{t(labelKey)}</span>
            </Link>
          ))}
        </nav>

        <div className="sidebar-footer">
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
      </aside>

      <header className="topbar">
        <Suspense fallback={<div className="search-field" />}>
          <SearchBar />
        </Suspense>
      </header>

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
      </nav>
    </div>
  );
}
