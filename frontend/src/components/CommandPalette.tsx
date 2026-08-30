// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import * as DialogPrimitive from "@radix-ui/react-dialog";
import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { cn } from "@/lib/cn";
import { formatArtists } from "@/lib/format";
import { LOCALE_NAMES, type Locale } from "@/lib/i18n";
import { navigationEntries } from "@/lib/navigation";
import { queries } from "@/lib/queries";
import { isLight, PALETTES, setTheme, useTheme } from "@/lib/theme";
import { useToggleFavorite } from "@/lib/useToggleFavorite";
import { useAuth } from "@/contexts/AuthContext";
import { useI18n, useT } from "@/contexts/I18nContext";
import { usePlayer } from "@/contexts/PlayerContext";
import { useSleepTimer } from "@/contexts/SleepTimerContext";
import { useToast } from "@/contexts/ToastContext";
import { AlbumCover, ArtistCover, TrackCover } from "./Cover";
import { Dialog, DialogOverlay } from "./ui/dialog";
import { Overline } from "./ui/label";
import { ClockIcon, HeartIcon, InfoIcon, MoonIcon, RadioIcon, SearchIcon, SunIcon } from "./Icons";

const DEBOUNCE_MS = 200;

const RESULT_LIMIT = 5;

const SLEEP_PRESETS = [15, 30, 60];

interface PaletteItem {
  id: string;
  label: string;
  hint?: string;
  art: ReactNode;
  run: () => void;
}

interface PaletteGroup {
  title: string;
  items: PaletteItem[];
}

export function CommandPalette({
  onClose,
  onOpenShortcuts,
}: {
  onClose: () => void;
  onOpenShortcuts: () => void;
}) {
  const t = useT();
  const router = useRouter();
  const player = usePlayer();
  const sleep = useSleepTimer();
  const { locale, setLocale } = useI18n();
  const { isAdmin } = useAuth();
  const { notify } = useToast();
  const theme = useTheme();
  const toggleFavorite = useToggleFavorite();

  const [input, setInput] = useState("");
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const [lastQuery, setLastQuery] = useState("");
  const listRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const timer = window.setTimeout(() => setQuery(input.trim()), DEBOUNCE_MS);
    return () => window.clearTimeout(timer);
  }, [input]);

  const results = useQuery(queries.search(query, RESULT_LIMIT));

  const go = (href: string) => {
    router.push(href);
    onClose();
  };

  const otherLocale: Locale = locale === "ru" ? "en" : "ru";

  const currentTrack = player.currentTrack;

  const startRadio = async () => {
    if (!currentTrack) return;
    onClose();

    await player.startDj("Flow", currentTrack);
  };

  const likeCurrent = () => {
    if (!currentTrack) return;

    onClose();
    void toggleFavorite(currentTrack);
  };

  // Разделы отдельной группой: раньше они лежали среди действий и тринадцать пунктов
  // подряд начинались с «Перейти:» — префикс занимал строку и ничего не различал.
  const navigation: PaletteItem[] = navigationEntries(isAdmin).map((entry) => ({
    id: `nav:${entry.href}`,
    label: t(entry.labelKey),
    art: <entry.icon size={16} />,
    run: () => go(entry.href),
  }));

  const actions: PaletteItem[] = [
    {
      id: "theme",
      label: t("palette.toggleTheme"),
      art: isLight(theme) ? <MoonIcon size={16} /> : <SunIcon size={16} />,
      run: () => {
        setTheme(PALETTES[(PALETTES.indexOf(theme) + 1) % PALETTES.length]);
        onClose();
      },
    },
    {
      id: "locale",
      label: t("palette.switchLanguage", { language: LOCALE_NAMES[otherLocale] }),
      art: <span className="text-xs font-bold uppercase">{otherLocale}</span>,
      run: () => {
        setLocale(otherLocale);
        onClose();
      },
    },
    {
      id: "shortcuts",
      label: t("shortcuts.show"),
      art: <InfoIcon size={16} />,
      run: () => {
        onClose();
        onOpenShortcuts();
      },
    },
    ...(currentTrack
      ? [
          {
            id: "radio",
            label: t("palette.radioFromCurrent"),
            hint: currentTrack.title,
            art: <RadioIcon size={16} />,
            run: () => void startRadio(),
          },
          {
            id: "favorite",
            label: currentTrack.isFavorite ? t("palette.unlikeCurrent") : t("palette.likeCurrent"),
            hint: currentTrack.title,
            art: <HeartIcon size={16} filled={currentTrack.isFavorite} />,
            run: likeCurrent,
          },
        ]
      : []),
    ...SLEEP_PRESETS.map((minutes) => ({
      id: `sleep:${minutes}`,
      label: `${t("palette.sleepTimer")} — ${t("sleep.minutes", { count: minutes })}`,
      art: <ClockIcon size={16} />,
      run: () => {
        sleep.startTimer(minutes);
        notify(t("sleep.set", { minutes }), "success");
        onClose();
      },
    })),
    {
      id: "sleep:track",
      label: `${t("palette.sleepTimer")} — ${t("sleep.endOfTrack")}`,
      art: <ClockIcon size={16} />,
      run: () => {
        sleep.stopAfterTrack();
        notify(t("sleep.setTrack"), "success");
        onClose();
      },
    },
    ...(sleep.plan.kind === "off"
      ? []
      : [
          {
            id: "sleep:off",
            label: `${t("palette.sleepTimer")} — ${t("sleep.off")}`,
            art: <ClockIcon size={16} />,
            run: () => {
              sleep.cancel();
              notify(t("sleep.cancelled"), "info");
              onClose();
            },
          },
        ]),
  ];

  const needle = query.toLowerCase();

  const matching = (items: PaletteItem[]) =>
    needle ? items.filter((item) => item.label.toLowerCase().includes(needle)) : items;

  const buildGroups = (): PaletteGroup[] => {
    const matched = matching(actions);
    const places = matching(navigation);

    const found = results.data;

    return [
      ...(found && needle
        ? [
            {
              title: t("palette.tracks"),
              items: found.tracks.map((track) => ({
                id: `track:${track.id}`,
                label: track.title,
                hint: formatArtists(track),
                art: <TrackCover track={track} size={32} />,
                run: () => {
                  player.playTrack(track, undefined, { source: "search" });
                  onClose();
                },
              })),
            },
            {
              title: t("palette.albums"),
              items: found.albums.map((album) => ({
                id: `album:${album.id}`,
                label: album.title,
                hint: album.artistName,
                art: <AlbumCover album={album} size={32} />,
                run: () => go(`/albums/${album.id}`),
              })),
            },
            {
              title: t("palette.artists"),
              items: found.artists.map((artist) => ({
                id: `artist:${artist.id}`,
                label: artist.name,
                art: <ArtistCover artist={artist} size={32} />,
                run: () => go(`/artists/${artist.id}`),
              })),
            },
          ]
        : []),
      { title: t("palette.actions"), items: matched },
      { title: t("palette.navigation"), items: places },
    ].filter((group) => group.items.length > 0);
  };

  const groups = buildGroups();

  const flat = groups.flatMap((group) => group.items);

  if (query !== lastQuery) {
    setLastQuery(query);
    setActive(0);
  }

  useEffect(() => {
    listRef.current?.querySelector("[data-active='true']")?.scrollIntoView({ block: "nearest" });
  }, [active]);

  const onKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setActive((index) => (flat.length === 0 ? 0 : (index + 1) % flat.length));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setActive((index) => (flat.length === 0 ? 0 : (index - 1 + flat.length) % flat.length));
    } else if (event.key === "Enter") {
      event.preventDefault();
      flat[active]?.run();
    }
  };

  let cursor = -1;

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogPrimitive.Portal>
        <DialogOverlay className="items-start pt-[12vh] max-md:pt-[8vh]">
          <DialogPrimitive.Content
            aria-describedby={undefined}
            onKeyDown={onKeyDown}
            className={cn(
              "relative flex max-h-[70dvh] w-[min(38rem,100%)] flex-col overflow-hidden rounded-xl bg-popover text-popover-foreground shadow-pop",
              "data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95",
              "data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-95",
            )}
          >
            <DialogPrimitive.Title className="sr-only">{t("palette.title")}</DialogPrimitive.Title>

            <div className="flex items-center gap-2.5 border-b border-border px-4 text-muted-foreground">
              <SearchIcon size={16} />
              <input
                autoFocus
                type="text"
                value={input}
                autoComplete="off"
                aria-label={t("palette.placeholder")}
                placeholder={t("palette.placeholder")}
                onChange={(event) => setInput(event.target.value)}
                className="w-full min-w-0 bg-transparent py-3.5 text-base text-foreground outline-none placeholder:text-faint"
              />
            </div>

            <div ref={listRef} className="min-h-0 flex-1 overflow-y-auto p-2">
              {flat.length === 0 ? (
                <p className="p-6 text-center text-muted-foreground">{t("palette.nothingFound")}</p>
              ) : (
                groups.map((group) => (
                  <section key={group.title} className="mb-2 flex flex-col gap-0.5">
                    <Overline className="px-2 py-1.5">{group.title}</Overline>

                    {group.items.map((item) => {
                      cursor += 1;
                      const index = cursor;

                      return (
                        <button
                          key={item.id}
                          type="button"
                          data-active={index === active ? "true" : undefined}
                          onMouseMove={() => setActive(index)}
                          onClick={item.run}
                          className={cn(
                            "flex items-center gap-3 rounded-md px-2 py-1.5 text-left",
                            "data-[active=true]:bg-accent",
                          )}
                        >
                          <span className="grid size-8 shrink-0 place-items-center text-muted-foreground">
                            {item.art}
                          </span>
                          <span className="flex min-w-0 flex-col">
                            <span className="truncate text-sm font-semibold">{item.label}</span>
                            {item.hint && (
                              <span className="truncate text-xs text-muted-foreground">
                                {item.hint}
                              </span>
                            )}
                          </span>
                        </button>
                      );
                    })}
                  </section>
                ))
              )}
            </div>
          </DialogPrimitive.Content>
        </DialogOverlay>
      </DialogPrimitive.Portal>
    </Dialog>
  );
}
