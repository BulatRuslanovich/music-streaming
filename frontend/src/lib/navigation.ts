// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { Route } from "next";
import type { TranslationKey } from "@/lib/i18n";
import {
  AudioLinesIcon,
  Disc3Icon,
  HeartIcon,
  HistoryIcon,
  HouseIcon,
  ListMusicIcon,
  SearchIcon,
  SettingsIcon,
  ShieldCheckIcon,
  TagsIcon,
  UploadIcon,
  UsersRoundIcon,
  type LucideIcon,
} from "lucide-react";

export interface NavEntry {
  href: Route;
  labelKey: TranslationKey;
  icon: LucideIcon;
}

/** Вкладки нижней панели на телефоне и первая группа сайдбара: их помещается четыре. */
export const primaryNav: NavEntry[] = [
  { href: "/", labelKey: "nav.home", icon: HouseIcon },
  { href: "/search", labelKey: "nav.search", icon: SearchIcon },
  { href: "/tracks", labelKey: "nav.tracks", icon: AudioLinesIcon },
  { href: "/playlists", labelKey: "nav.playlists", icon: ListMusicIcon },
];

export const libraryNav: NavEntry[] = [
  { href: "/favorites", labelKey: "nav.favorites", icon: HeartIcon },
  { href: "/albums", labelKey: "nav.albums", icon: Disc3Icon },
  { href: "/artists", labelKey: "nav.artists", icon: UsersRoundIcon },
  { href: "/genres", labelKey: "nav.genres", icon: TagsIcon },
  { href: "/recently-played", labelKey: "nav.recentlyPlayed", icon: HistoryIcon },
];

export function serviceNav(isAdmin: boolean): NavEntry[] {
  return [
    { href: "/upload", labelKey: "nav.upload", icon: UploadIcon },
    { href: "/settings", labelKey: "nav.settings", icon: SettingsIcon },
    ...(isAdmin ? [{ href: "/admin", labelKey: "nav.admin", icon: ShieldCheckIcon } as const] : []),
  ];
}

export function isActivePath(pathname: string, href: string): boolean {
  return href === "/" ? pathname === "/" : pathname === href || pathname.startsWith(`${href}/`);
}
