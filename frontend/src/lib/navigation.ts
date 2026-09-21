// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { Route } from "next";
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
  TagIcon,
  UploadIcon,
  type IconProps,
} from "@/components/Icons";

export interface NavEntry {
  href: Route;
  labelKey: TranslationKey;
  icon: (props: IconProps) => ReactNode;
}

/**
 * Три группы, а не две: у сайдбара и нижней панели разные ограничения. На телефоне вкладок
 * физически помещается четыре — это `primaryNav`.
 */
export const primaryNav: NavEntry[] = [
  { href: "/", labelKey: "nav.home", icon: HomeIcon },
  { href: "/search", labelKey: "nav.search", icon: SearchIcon },
  // `nav.library` («Ваша библиотека») теперь заголовок секции ниже, а не подпись раздела.
  { href: "/tracks", labelKey: "nav.tracks", icon: LibraryIcon },
  { href: "/playlists", labelKey: "nav.playlists", icon: PlaylistIcon },
];

const favorites: NavEntry = { href: "/favorites", labelKey: "nav.favorites", icon: HeartIcon };

const recentlyPlayed: NavEntry = {
  href: "/recently-played",
  labelKey: "nav.recentlyPlayed",
  icon: HistoryIcon,
};

/**
 * Способы перебрать библиотеку по граням: альбомы, исполнители, жанры, теги. В сайдбаре они
 * уехали под «Ещё» — четыре строки одного рода занимали больше места, чем всё остальное меню,
 * а возвращаются к ним реже, чем к избранному и к недавнему.
 *
 * Плата известна и признана: переход в «Альбомы» снова стоит два клика. Поэтому пункты
 * дропдауна греют свои запросы по наведению так же, как обычные ссылки сайдбара, — задержка
 * от лишнего клика съедается предзагрузкой.
 */
export const catalogNav: NavEntry[] = [
  { href: "/albums", labelKey: "nav.albums", icon: AlbumIcon },
  { href: "/artists", labelKey: "nav.artists", icon: ArtistIcon },
  { href: "/genres", labelKey: "nav.genres", icon: GenreIcon },
  { href: "/tags", labelKey: "nav.tags", icon: TagIcon },
];

/** То, что остаётся в сайдбаре плоским списком: две ссылки, к которым возвращаются каждый день. */
export const shortcutNav: NavEntry[] = [favorites, recentlyPlayed];

/**
 * Каталог целиком. Нужен шторке «Ещё» на телефоне и палитре команд, где места хватает;
 * сайдбар показывает выборку.
 */
export const libraryNav: NavEntry[] = [favorites, ...catalogNav, recentlyPlayed];

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
