"use client";

import Image from "next/image";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { Suspense, useEffect, useState } from "react";
import { useAuth } from "@/contexts/AuthContext";
import { Player } from "./Player";
import { SearchBar } from "./SearchBar";
import {
  AlbumIcon,
  ArtistIcon,
  ClockIcon,
  HeartIcon,
  HomeIcon,
  LibraryIcon,
  NoteIcon,
  PlaylistIcon,
  SearchIcon,
  ShieldIcon,
  SignOutIcon,
  UploadIcon,
} from "./Icons";

const primaryNav = [
  { href: "/tracks", label: "Tracks", icon: NoteIcon },
  { href: "/albums", label: "Albums", icon: AlbumIcon },
  { href: "/artists", label: "Artists", icon: ArtistIcon },
  { href: "/genres", label: "Genres", icon: LibraryIcon },
];

const libraryNav = [
  { href: "/favorites", label: "My collections", icon: HeartIcon },
  { href: "/playlists", label: "Playlists", icon: PlaylistIcon },
  { href: "/recently-played", label: "Recently played", icon: ClockIcon },
  { href: "/upload", label: "Upload", icon: UploadIcon },
];

const adminNav = { href: "/admin", label: "Admin", icon: ShieldIcon };

const mobileNav = [
  { href: "/search", label: "Search", icon: SearchIcon },
  { href: "/playlists", label: "Playlists", icon: PlaylistIcon },
  { href: "/favorites", label: "My collections", icon: HeartIcon },
  { href: "/upload", label: "Upload", icon: UploadIcon },
];


export function AppShell({ children }: { children: React.ReactNode }) {
  const { user, isAdmin, loading, signOut } = useAuth();
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
        <p className="muted">Loading your library…</p>
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
          <Link href="/" aria-label="Home">
            <Image className="brand-logo" src="/logo.png" alt="" width={34} height={34} priority />
          </Link>

          <span className="brand-text">CAIMACK</span>
        </div>

        <nav aria-label="Browse">
          {primaryNav.map(({ href, label, icon: Icon }) => (
            <Link key={href} href={href} className={`nav-link ${isActive(href) ? "is-active" : ""}`}>
              <Icon size={19} />
              <span>{label}</span>
            </Link>
          ))}
        </nav>

        <p className="nav-heading">Your library</p>
        <nav aria-label="Your library">
          {libraryLinks.map(({ href, label, icon: Icon }) => (
            <Link key={href} href={href} className={`nav-link ${isActive(href) ? "is-active" : ""}`}>
              <Icon size={19} />
              <span>{label}</span>
            </Link>
          ))}
        </nav>

        <div className="sidebar-footer">
          <span className="user-chip" title={user.username}>
            {user.displayName || user.username}
          </span>
          <button
            type="button"
            className="icon-button"
            onClick={() => {
              setSigningOut(true);
              void signOut().finally(() => setSigningOut(false));
            }}
            disabled={signingOut}
            aria-label="Sign out"
            title="Sign out"
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

      <nav className="mobile-nav" aria-label="Main">
        {mobileNav.map(({ href, label, icon: Icon }) => (
          <Link
            key={href}
            href={href}
            className={`mobile-nav-link ${isActive(href) ? "is-active" : ""}`}
            aria-current={isActive(href) ? "page" : undefined}
          >
            <Icon size={20} />
            <span>{label}</span>
          </Link>
        ))}
      </nav>
    </div>
  );
}
