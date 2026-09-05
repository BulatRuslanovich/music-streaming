// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { ReactNode } from "react";
import type { TranslationKey } from "@/lib/i18n";
import {
  AlbumIcon,
  ArtistIcon,
  ChartIcon,
  GenreIcon,
  HeartIcon,
  HistoryIcon,
  HomeIcon,
  LibraryIcon,
  PlaylistIcon,
  SearchIcon,
  SettingsIcon,
  ShieldIcon,
  SparkleIcon,
  UploadIcon,
  type IconProps,
} from "@/components/Icons";

export interface NavEntry {
  href: string;
  labelKey: TranslationKey;
  icon: (props: IconProps) => ReactNode;
}

/**
 * Три группы, а не две: у сайдбара и нижней панели разные ограничения. На телефоне вкладок
 * физически помещается четыре — это `primaryNav`. В сайдбаре вертикального места вагон,
 * поэтому каталог живёт там плоско (`libraryNav`), а не за дропдауном: раньше переход в
 * «Альбомы» стоил два клика и попадание в иконку 19px при пустой колонке в 232px.
 */
export const primaryNav: NavEntry[] = [
  { href: "/", labelKey: "nav.home", icon: HomeIcon },
  { href: "/search", labelKey: "nav.search", icon: SearchIcon },
  // `nav.library` («Ваша библиотека») теперь заголовок секции ниже, а не подпись раздела.
  { href: "/tracks", labelKey: "nav.tracks", icon: LibraryIcon },
  { href: "/playlists", labelKey: "nav.playlists", icon: PlaylistIcon },
];

/** Разделы каталога: в сайдбаре — второй секцией, на телефоне — в шторке «Ещё». */
export const libraryNav: NavEntry[] = [
  { href: "/favorites", labelKey: "nav.favorites", icon: HeartIcon },
  { href: "/albums", labelKey: "nav.albums", icon: AlbumIcon },
  { href: "/artists", labelKey: "nav.artists", icon: ArtistIcon },
  { href: "/genres", labelKey: "nav.genres", icon: GenreIcon },
  { href: "/recently-played", labelKey: "nav.recentlyPlayed", icon: HistoryIcon },
];

/** Всё, к чему возвращаются редко. */
export const moreNav: NavEntry[] = [
  { href: "/statistics", labelKey: "nav.stats", icon: ChartIcon },
  { href: "/upload", labelKey: "nav.upload", icon: UploadIcon },
  { href: "/settings", labelKey: "nav.settings", icon: SettingsIcon },
];

export const adminNav: NavEntry = { href: "/admin", labelKey: "nav.admin", icon: ShieldIcon };

/**
 * Итоги месяца — единственный пункт, который приходит и уходит: он живёт первые семь дней
 * месяца. Постоянная ссылка вела бы двадцать дней на редирект, поэтому раздел появляется
 * ровно тогда, когда за ним что-то есть.
 */
export const recapNav: NavEntry = { href: "/recap", labelKey: "nav.recap", icon: SparkleIcon };

export function moreEntries(recapOpen: boolean): NavEntry[] {
  return recapOpen ? [recapNav, ...moreNav] : moreNav;
}

export function navigationEntries(isAdmin: boolean, recapOpen = false): NavEntry[] {
  return [...primaryNav, ...libraryNav, ...moreEntries(recapOpen), ...(isAdmin ? [adminNav] : [])];
}
