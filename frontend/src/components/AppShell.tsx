"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { useAuth } from "@/contexts/AuthContext";
import { Player } from "./Player";
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
  SignOutIcon,
  UploadIcon,
} from "./Icons";

const primaryNav = [
  { href: "/", label: "Home", icon: HomeIcon },
  { href: "/search", label: "Search", icon: SearchIcon },
  { href: "/tracks", label: "Tracks", icon: NoteIcon },
  { href: "/albums", label: "Albums", icon: AlbumIcon },
  { href: "/artists", label: "Artists", icon: ArtistIcon },
  { href: "/genres", label: "Genres", icon: LibraryIcon },
];

const libraryNav = [
  { href: "/playlists", label: "Playlists", icon: PlaylistIcon },
  { href: "/favorites", label: "Favourites", icon: HeartIcon },
  { href: "/recently-played", label: "Recently played", icon: ClockIcon },
  { href: "/upload", label: "Upload", icon: UploadIcon },
];

/** The tabs that fit a phone's bottom bar; the rest stay reachable from Home. */
const mobileNav = [
  { href: "/", label: "Home", icon: HomeIcon },
  { href: "/search", label: "Search", icon: SearchIcon },
  { href: "/playlists", label: "Playlists", icon: PlaylistIcon },
  { href: "/favorites", label: "Favourites", icon: HeartIcon },
  { href: "/upload", label: "Upload", icon: UploadIcon },
];

/**
 * Frame shared by every authenticated page: navigation, the scrolling content column and the
 * persistent player. Because this lives in the root layout, the player's audio element is never
 * unmounted by a route change.
 */
export function AppShell({ children }: { children: React.ReactNode }) {
  const { user, loading, signOut } = useAuth();
  const pathname = usePathname();
  const router = useRouter();

  const isLoginPage = pathname === "/login";
  const [signingOut, setSigningOut] = useState(false);

  // Everything except the login page requires a session.
  useEffect(() => {
    if (!loading && !user && !isLoginPage) {
      router.replace("/login");
    }
  }, [loading, user, isLoginPage, router]);

  // A signed-in user has no reason to sit on the login form.
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
    // The redirect above is already in flight; render nothing rather than a flash of the shell.
    return null;
  }

  const isActive = (href: string) =>
    href === "/" ? pathname === "/" : pathname === href || pathname.startsWith(`${href}/`);

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-mark" aria-hidden="true">
            <NoteIcon size={20} />
          </span>
          <span className="brand-text">My Music</span>
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
          {libraryNav.map(({ href, label, icon: Icon }) => (
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
