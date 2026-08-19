// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { ReactNode } from "react";
import type { TranslationKey } from "@/lib/i18n";
import {
  AlbumIcon,
  ArtistIcon,
  ChartIcon,
  ClockIcon,
  HeartIcon,
  LibraryIcon,
  NoteIcon,
  PlaylistIcon,
  SearchIcon,
  SettingsIcon,
  ShieldIcon,
  UploadIcon,
  type IconProps,
} from "@/components/Icons";

export interface NavEntry {
  href: string;
  labelKey: TranslationKey;
  icon: (props: IconProps) => ReactNode;
}

export const searchNav: NavEntry = { href: "/search", labelKey: "nav.search", icon: SearchIcon };

export const primaryNav: NavEntry[] = [
  { href: "/tracks", labelKey: "nav.tracks", icon: NoteIcon },
  { href: "/albums", labelKey: "nav.albums", icon: AlbumIcon },
  { href: "/artists", labelKey: "nav.artists", icon: ArtistIcon },
  { href: "/genres", labelKey: "nav.genres", icon: LibraryIcon },
];

export const libraryNav: NavEntry[] = [
  { href: "/favorites", labelKey: "nav.favorites", icon: HeartIcon },
  { href: "/playlists", labelKey: "nav.playlists", icon: PlaylistIcon },
  { href: "/recently-played", labelKey: "nav.recentlyPlayed", icon: ClockIcon },
  { href: "/statistics", labelKey: "nav.stats", icon: ChartIcon },
  { href: "/upload", labelKey: "nav.upload", icon: UploadIcon },
  { href: "/settings", labelKey: "nav.settings", icon: SettingsIcon },
];

export const adminNav: NavEntry = { href: "/admin", labelKey: "nav.admin", icon: ShieldIcon };

export const mobileNav: NavEntry[] = [
  { href: "/tracks", labelKey: "nav.tracks", icon: NoteIcon },
  { href: "/search", labelKey: "nav.search", icon: SearchIcon },
  { href: "/favorites", labelKey: "nav.favorites", icon: HeartIcon },
  { href: "/playlists", labelKey: "nav.playlists", icon: PlaylistIcon },
];

export const mobileSheetNav: NavEntry[] = [
  { href: "/albums", labelKey: "nav.albums", icon: AlbumIcon },
  { href: "/artists", labelKey: "nav.artists", icon: ArtistIcon },
  { href: "/genres", labelKey: "nav.genres", icon: LibraryIcon },
  ...libraryNav.slice(2),
];
