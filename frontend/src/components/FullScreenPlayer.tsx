// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import * as DialogPrimitive from "@radix-ui/react-dialog";
import Link from "next/link";
import { useState } from "react";
import { cn } from "@/lib/cn";
import { formatDuration } from "@/lib/format";
import { useIdle } from "@/lib/useIdle";
import { useInvalidate } from "@/lib/useInvalidate";
import { usePlaybackProgress } from "@/lib/playback/usePlaybackProgress";
import { usePlayer } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { ArtistLinks } from "./ArtistLinks";
import { Record } from "./Record";
import { PlayerTransport } from "./PlayerTransport";
import { PlayerVolume } from "./PlayerVolume";
import { DataSaverToggle } from "./DataSaverToggle";
import { Seekbar } from "./Seekbar";
import { LyricsPane } from "./LyricsPane";
import { QueueList } from "./QueuePanel";
import { TrackMenu } from "./TrackMenu";
import { Button } from "./ui/button";
import { EllipsisVerticalIcon, HeartIcon, ListVideoIcon, MicVocalIcon, XIcon } from "lucide-react";

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
  const progress = usePlaybackProgress(fallbackDuration);

  return (
    <div className="flex flex-col gap-0.5">
      <Seekbar
        value={progress.position}
        max={progress.total}
        onSeek={progress.seek}
        ariaLabel={progress.seekLabel}
        tooltip={formatDuration}
        commitOnRelease
      />
      <div
        className={cn("flex justify-between text-xs text-muted-foreground tabular-nums", chrome)}
      >
        <span>{formatDuration(progress.position)}</span>
        <button
          type="button"
          onClick={progress.toggleRemainingTime}
          aria-label={progress.toggleRemainingLabel}
          className="rounded-sm tabular-nums hover:text-foreground"
        >
          {progress.endLabel}
        </button>
      </div>
    </div>
  );
}

export function FullScreenPlayer({
  onClose,
  onToggleFavorite,
}: {
  onClose: () => void;
  onToggleFavorite: () => void;
}) {
  const player = usePlayer();
  const { notify } = useToast();
  const invalidate = useInvalidate();
  const t = useT();
  const [panel, setPanel] = useState<"art" | "queue" | "lyrics">("art");
  const [menuOpen, setMenuOpen] = useState(false);
  const track = player.currentTrack;
  const idle = useIdle(IDLE_MS, panel === "art" && !menuOpen);

  const chrome = cn(
    "transition-opacity duration-300 ease-brand focus-within:opacity-100",
    idle && "opacity-0",
  );

  if (!track) return null;

  return (
    // Radix, а не самодельный оверлей: `role="dialog" aria-modal="true"` на div
    // объявляет модальность, но не даёт её — фокус не переносится внутрь, табом можно уйти
    // на страницу под ним, фон не скрыт от скринридера. Escape и возврат фокуса тоже отсюда.
    <DialogPrimitive.Root open onOpenChange={(next) => !next && onClose()}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Content asChild aria-describedby={undefined}>
          <div
            // Горячие клавиши плеера пропускают открытые оверлеи по [data-state=open]; этот
            // экран сам и есть плеер, поэтому он помечен как исключение.
            data-player-fullscreen="true"
            className="fixed inset-0 z-90 flex animate-in flex-col bg-background duration-300 fade-in-0 slide-in-from-bottom-6 px-5 pt-[max(1rem,env(safe-area-inset-top))] pb-[max(1.25rem,env(safe-area-inset-bottom))]"
          >
            <DialogPrimitive.Title className="sr-only">
              {t("player.nowPlaying")}
            </DialogPrimitive.Title>

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
                <XIcon size={20} />
              </Button>

              <span className="text-sm text-muted-foreground">{t("player.nowPlaying")}</span>

              <div className="flex items-center gap-1 justify-self-end">
                <DataSaverToggle size="icon-lg" />

                <Button
                  variant="ghost"
                  size="icon-lg"
                  className={cn(panel === "lyrics" && "text-primary")}
                  onClick={() => setPanel((open) => (open === "lyrics" ? "art" : "lyrics"))}
                  aria-label={panel === "lyrics" ? t("lyrics.hide") : t("lyrics.show")}
                  aria-pressed={panel === "lyrics"}
                  title={t("lyrics.title")}
                >
                  <MicVocalIcon size={20} />
                </Button>

                <Button
                  variant="ghost"
                  size="icon-lg"
                  className={cn(panel === "queue" && "text-primary")}
                  onClick={() => setPanel((open) => (open === "queue" ? "art" : "queue"))}
                  aria-label={t("queue.label")}
                  aria-pressed={panel === "queue"}
                >
                  <ListVideoIcon size={20} />
                </Button>
              </div>
            </header>

            {panel === "queue" ? (
              <div className="relative z-1 flex-1 overflow-y-auto pt-3">
                <QueueList />
              </div>
            ) : (
              <div className="relative z-1 flex min-h-0 flex-1 gap-[clamp(2rem,5vw,5rem)] max-lg:flex-col max-lg:overflow-y-auto">
                {panel === "art" && (
                  // Справа от конверта оставлен запас под выехавший диск: без него он уходил
                  // бы под колонку с названием. На узком экране диск выдвигается вверх.
                  <div className="flex min-h-0 flex-[1.2] items-center justify-end pr-[min(40%,20vh)] max-lg:flex-none max-lg:justify-center max-lg:pt-[18%] max-lg:pr-0">
                    <Record
                      track={track}
                      out
                      spinning={player.isPlaying}
                      axis="x"
                      sizes="min(48vh, 28rem)"
                      className="w-[min(100%,48vh)] max-lg:hidden"
                    />
                    <Record
                      track={track}
                      out
                      spinning={player.isPlaying}
                      axis="y"
                      className="w-[min(72%,20rem)] [--record-out:30%] lg:hidden"
                    />
                  </div>
                )}

                <div
                  className={cn(
                    "flex min-h-0 flex-1 flex-col justify-center gap-6 max-lg:flex-none max-lg:pb-4",
                    panel === "lyrics" && "max-lg:hidden lg:max-w-[50%] lg:flex-none lg:basis-1/2",
                  )}
                >
                  {/* Рядом с декой колонка прижата к пластинке; рядом с текстом песни ей своя
                      половина экрана, и в ней она стоит по центру, а не у кромки. */}
                  <div
                    className={cn(
                      "flex w-full max-w-[34rem] flex-col gap-6 max-lg:mx-auto",
                      panel === "lyrics" && "lg:mx-auto",
                    )}
                  >
                    <div className="flex min-w-0 flex-col gap-2">
                      <h2 className="line-clamp-3 font-display text-display text-balance">
                        {track.title}
                      </h2>
                      <p className="text-muted-foreground">
                        <ArtistLinks
                          track={track}
                          onNavigate={onClose}
                          className="font-medium text-foreground"
                        />
                        {track.albumId && (
                          <>
                            {", "}
                            <Link href={`/albums/${track.albumId}`} onClick={onClose}>
                              {track.albumTitle}
                            </Link>
                          </>
                        )}
                      </p>
                    </div>

                    <FullScreenProgress fallbackDuration={track.durationSeconds} chrome={chrome} />

                    <PlayerTransport size="full" />

                    <div className={cn("flex items-center gap-2", chrome)}>
                      <Button
                        variant="ghost"
                        size="icon-lg"
                        className={cn(track.isFavorite && "text-primary hover:text-primary")}
                        onClick={onToggleFavorite}
                        aria-label={
                          track.isFavorite
                            ? t("tracks.removeFromFavorites")
                            : t("tracks.addToFavorites")
                        }
                        aria-pressed={track.isFavorite}
                      >
                        <HeartIcon className={track.isFavorite ? "fill-current" : undefined} />
                      </Button>

                      <TrackMenu
                        track={track}
                        open={menuOpen}
                        onOpenChange={setMenuOpen}
                        onChanged={() => invalidate("library", "playlists")}
                        onNavigate={onClose}
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
                            aria-label={t("tracks.moreActions", { title: track.title })}
                          >
                            <EllipsisVerticalIcon />
                          </Button>
                        }
                      />

                      <div className="ml-auto flex w-[12.5rem] max-w-[50%] items-center gap-2">
                        <PlayerVolume size="icon-lg" />
                      </div>
                    </div>
                  </div>
                </div>

                {panel === "lyrics" && (
                  <div className="min-h-0 flex-1 overflow-y-auto scroll-smooth px-4 [scrollbar-width:none] motion-reduce:scroll-auto [&::-webkit-scrollbar]:hidden">
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
          </div>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}
