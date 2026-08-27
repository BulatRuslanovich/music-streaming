// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import * as DialogPrimitive from "@radix-ui/react-dialog";
import { motion, useReducedMotion } from "motion/react";
import Link from "next/link";
import { ReactNode, useState } from "react";
import { cn } from "@/lib/cn";
import { formatDuration } from "@/lib/format";
import { trackCoverUrl } from "@/lib/media";
import { useIdle } from "@/lib/useIdle";
import { useInvalidate } from "@/lib/useInvalidate";
import { usePlaylistsOnce } from "@/lib/usePlaylistsOnce";
import { toggleRemainingTime, useRemainingTime } from "@/lib/useRemainingTime";
import { usePlayer, usePlayerActions, usePlayerProgress } from "@/contexts/PlayerContext";
import { useSettings } from "@/contexts/SettingsContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { DURATION, EASE } from "@/lib/motion";
import { CoverBackdrop } from "./AmbientBackdrop";
import { ArtistLinks } from "./ArtistLinks";
import { TrackCover } from "./Cover";
import { Seekbar } from "./Seekbar";
import { LyricsPane } from "./LyricsPane";
import { QueueList } from "./QueuePanel";
import { TrackMenu } from "./TrackMenu";
import { Button } from "./ui/button";
import {
  CloseIcon,
  DataSaverIcon,
  HeartIcon,
  LyricsIcon,
  MoreIcon,
  MuteIcon,
  QueueIcon,
  VolumeIcon,
} from "./Icons";

const artButton = "rounded-full bg-black/45 backdrop-blur-sm hover:bg-black/65";

const IDLE_MS = 2500;

/**
 * Полоса и часы отдельным компонентом: контекст прогресса тикает 4 раза в секунду, а этот
 * экран держит внутри себя очередь целиком — перерисовывать её ради бегущей секунды незачем.
 */
function FullScreenProgress({
  fallbackDuration,
  chrome,
}: {
  fallbackDuration: number;
  chrome: string;
}) {
  const { position, duration } = usePlayerProgress();
  const { seek } = usePlayerActions();
  const showRemaining = useRemainingTime();
  const t = useT();

  const total = duration || fallbackDuration;

  return (
    <div className="flex flex-col gap-0.5">
      <Seekbar
        value={position}
        max={total}
        onSeek={seek}
        ariaLabel={t("player.seek")}
        tooltip={formatDuration}
        commitOnRelease
      />
      <div
        className={cn("flex justify-between text-xs text-muted-foreground tabular-nums", chrome)}
      >
        <span>{formatDuration(position)}</span>
        <button
          type="button"
          onClick={toggleRemainingTime}
          aria-label={t("player.toggleRemaining")}
          className="rounded-sm tabular-nums hover:text-foreground"
        >
          {showRemaining
            ? `-${formatDuration(Math.max(0, total - position))}`
            : formatDuration(total)}
        </button>
      </div>
    </div>
  );
}

export function FullScreenPlayer({
  onClose,
  transport,
  onToggleFavorite,
}: {
  onClose: () => void;
  transport: ReactNode;
  onToggleFavorite: () => void;
}) {
  const player = usePlayer();
  const settings = useSettings();
  const { notify } = useToast();
  const invalidate = useInvalidate();
  const t = useT();
  const [panel, setPanel] = useState<"art" | "queue" | "lyrics">("art");
  const [menuOpen, setMenuOpen] = useState(false);
  const track = player.currentTrack;
  const reduceMotion = useReducedMotion();
  const idle = useIdle(IDLE_MS, panel === "art" && !menuOpen);

  const chrome = cn(
    "transition-opacity duration-300 ease-brand focus-within:opacity-100",
    idle && "opacity-0",
  );

  const loadPlaylists = usePlaylistsOnce();

  if (!track) return null;

  return (
    // Radix вместо самодельного оверлея: раньше здесь стоял role="dialog" aria-modal="true"
    // на motion.div, но фокус внутрь не переносился, табом можно было уйти на страницу под
    // ним, а фон не скрывался от скринридера — то есть модальность объявлялась, но её не было.
    // Escape и возврат фокуса на кнопку тоже теперь его.
    <DialogPrimitive.Root open onOpenChange={(next) => !next && onClose()}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Content asChild aria-describedby={undefined}>
          <motion.div
            // Горячие клавиши плеера пропускают открытые оверлеи по [data-state=open]; этот
            // экран сам и есть плеер, поэтому он помечен как исключение.
            data-player-fullscreen="true"
            initial={reduceMotion ? false : { opacity: 0, y: 24 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: 24 }}
            transition={{ duration: DURATION * 1.5, ease: EASE }}
            className="fixed inset-0 z-90 flex flex-col bg-background px-5 pt-[max(1rem,env(safe-area-inset-top))] pb-[max(1.25rem,env(safe-area-inset-bottom))]"
          >
            <DialogPrimitive.Title className="sr-only">
              {t("player.nowPlaying")}
            </DialogPrimitive.Title>

            <CoverBackdrop source={trackCoverUrl(track, "thumb")} />

            <header
              className={cn(
                "relative z-1 grid shrink-0 grid-cols-[1fr_auto_1fr] items-center gap-3",
                chrome,
              )}
            >
              <Button
                variant="ghost"
                size="icon-lg"
                className="justify-self-start"
                onClick={onClose}
                aria-label={t("player.closeFull")}
              >
                <CloseIcon size={22} />
              </Button>

              <span className="text-sm text-muted-foreground">{t("player.nowPlaying")}</span>

              <div className="flex items-center gap-1 justify-self-end">
                {settings.qualities.length > 1 && (
                  <Button
                    variant="ghost"
                    size="icon-lg"
                    className={cn(settings.dataSaver && "text-primary")}
                    onClick={() => settings.update({ dataSaver: !settings.dataSaver })}
                    aria-label={t("player.dataSaver")}
                    aria-pressed={settings.dataSaver}
                  >
                    <DataSaverIcon size={20} />
                  </Button>
                )}

                <Button
                  variant="ghost"
                  size="icon-lg"
                  className={cn(panel === "lyrics" && "text-primary")}
                  onClick={() => setPanel((open) => (open === "lyrics" ? "art" : "lyrics"))}
                  aria-label={panel === "lyrics" ? t("lyrics.hide") : t("lyrics.show")}
                  aria-pressed={panel === "lyrics"}
                  title={t("lyrics.title")}
                >
                  <LyricsIcon size={20} />
                </Button>

                <Button
                  variant="ghost"
                  size="icon-lg"
                  className={cn(panel === "queue" && "text-primary")}
                  onClick={() => setPanel((open) => (open === "queue" ? "art" : "queue"))}
                  aria-label={t("queue.label")}
                  aria-pressed={panel === "queue"}
                >
                  <QueueIcon size={20} />
                </Button>
              </div>
            </header>

            {panel === "queue" ? (
              <div className="relative z-1 flex-1 overflow-y-auto pt-3">
                <QueueList />
              </div>
            ) : (
              <div className="relative z-1 flex min-h-0 flex-1">
                <div
                  className={cn(
                    "flex min-h-0 flex-1 justify-center",
                    panel === "lyrics" && "hidden lg:flex lg:w-1/2 lg:flex-none",
                  )}
                >
                  <div className="flex min-h-0 w-full max-w-[28.75rem] flex-col justify-center gap-5">
                    <div
                      data-menu={menuOpen ? "open" : undefined}
                      className="group relative aspect-square w-[min(100%,46vh)] shrink-0 self-center overflow-hidden rounded-xl shadow-art"
                    >
                      <TrackCover track={track} size="100%" variant="full" />

                      <div
                        className={cn(
                          "pointer-events-none absolute inset-0 flex flex-col p-3 opacity-0 transition-opacity duration-150 ease-brand",
                          "bg-[linear-gradient(180deg,transparent_55%,rgba(0,0,0,0.5))]",
                          !idle && "group-hover:pointer-events-auto group-hover:opacity-100",
                          "group-focus-within:pointer-events-auto group-focus-within:opacity-100",
                          "group-data-[menu=open]:pointer-events-auto group-data-[menu=open]:opacity-100",
                          "[@media(pointer:coarse)]:pointer-events-auto [@media(pointer:coarse)]:opacity-100",
                        )}
                      >
                        {/* Только контекстные действия: транспорт переехал вниз, в общий поток.
                      Под hover он был не виден при открытии экрана и закрывал собой арт. */}
                        <div className="mt-auto flex items-center justify-between">
                          <TrackMenu
                            track={track}
                            open={menuOpen}
                            onOpenChange={setMenuOpen}
                            onChanged={() => invalidate("library", "playlists")}
                            onNavigate={onClose}
                            loadPlaylists={loadPlaylists}
                            isFavorite={track.isFavorite}
                            onToggleFavorite={onToggleFavorite}
                            onQueue={() => {
                              player.addToQueue(track);
                              notify(t("menu.addedToQueue", { title: track.title }), "success");
                            }}
                            trigger={
                              <Button
                                variant="ghost"
                                size="icon-lg"
                                className={cn(artButton, "text-white hover:text-white")}
                                aria-label={t("tracks.moreActions", { title: track.title })}
                              >
                                <MoreIcon size={20} />
                              </Button>
                            }
                          />

                          <Button
                            variant="ghost"
                            size="icon-lg"
                            className={cn(
                              artButton,
                              track.isFavorite
                                ? "text-primary hover:text-primary"
                                : "text-white hover:text-white",
                            )}
                            onClick={onToggleFavorite}
                            aria-label={
                              track.isFavorite
                                ? t("tracks.removeFromFavorites")
                                : t("tracks.addToFavorites")
                            }
                            aria-pressed={track.isFavorite}
                          >
                            <HeartIcon size={20} filled={track.isFavorite} />
                          </Button>
                        </div>
                      </div>
                    </div>

                    <div className="flex flex-col gap-1 text-center">
                      <h2 className="text-2xl">{track.title}</h2>
                      <ArtistLinks track={track} onNavigate={onClose} />
                      {track.albumId && (
                        <Link
                          href={`/albums/${track.albumId}`}
                          className="text-muted-foreground"
                          onClick={onClose}
                        >
                          {track.albumTitle}
                        </Link>
                      )}
                    </div>

                    <FullScreenProgress fallbackDuration={track.durationSeconds} chrome={chrome} />

                    {transport}

                    <div
                      className={cn(
                        "mx-auto flex w-[12.5rem] max-w-full items-center gap-2",
                        chrome,
                      )}
                    >
                      <Button
                        variant="ghost"
                        size="icon-lg"
                        onClick={player.toggleMute}
                        aria-label={player.muted ? t("player.unmute") : t("player.mute")}
                      >
                        {player.muted || player.volume === 0 ? (
                          <MuteIcon size={20} />
                        ) : (
                          <VolumeIcon size={20} />
                        )}
                      </Button>
                      <Seekbar
                        value={player.muted ? 0 : player.volume}
                        max={1}
                        step={0.01}
                        onSeek={player.setVolume}
                        ariaLabel={t("player.volume")}
                      />
                    </div>
                  </div>
                </div>

                {panel === "lyrics" && (
                  <div className="min-h-0 flex-1 overflow-y-auto px-4 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
                    <LyricsPane
                      key={track.id}
                      track={track}
                      onSeek={player.seek}
                      onLyricsKnown={(hasLyrics) => player.patchTrack(track.id, { hasLyrics })}
                    />
                  </div>
                )}
              </div>
            )}
          </motion.div>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}
