"use client";

import { motion, useReducedMotion } from "motion/react";
import Link from "next/link";
import { ReactNode, useCallback, useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import { cn } from "@/lib/cn";
import { formatDuration } from "@/lib/format";
import { useInvalidate } from "@/lib/useInvalidate";
import type { Playlist } from "@/lib/types";
import { usePlayer, usePlayerProgress } from "@/contexts/PlayerContext";
import { useSettings } from "@/contexts/SettingsContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { DURATION, EASE } from "@/lib/motion";
import { ArtistLinks } from "./ArtistLinks";
import { Cover } from "./Cover";
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

/* Кнопки на обложке: тёмный кружок поверх любой картинки — по нему видно, что это кнопка. */
const artButton = "size-11 rounded-full bg-black/45 backdrop-blur-sm hover:bg-black/65";

export function FullScreenPlayer({
  onClose,
  transport,
  artTransport,
  onToggleFavorite,
}: {
  onClose: () => void;
  transport: ReactNode;
  /** Укороченный набор кнопок для накладки на обложку. */
  artTransport: ReactNode;
  onToggleFavorite: () => void;
}) {
  const player = usePlayer();
  const progress = usePlayerProgress();
  const settings = useSettings();
  const { notify } = useToast();
  const invalidate = useInvalidate();
  const t = useT();
  const [panel, setPanel] = useState<"art" | "queue" | "lyrics">("art");
  const [menuOpen, setMenuOpen] = useState(false);
  const track = player.currentTrack;
  const reduceMotion = useReducedMotion();

  // Список плейлистов нужен только меню «…» и только один раз за сеанс с ним.
  const playlistsRequest = useRef<Promise<Playlist[]> | null>(null);
  const loadPlaylists = useCallback(() => {
    playlistsRequest.current ??= api.playlists().catch(() => []);
    return playlistsRequest.current;
  }, []);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  if (!track) return null;

  const duration = progress.duration || track.durationSeconds;

  return (
    <motion.div
      role="dialog"
      aria-modal="true"
      aria-label={t("player.nowPlaying")}
      initial={reduceMotion ? false : { opacity: 0, y: 24 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: 24 }}
      transition={{ duration: DURATION * 1.5, ease: EASE }}
      className="fixed inset-0 z-90 flex flex-col bg-background px-5 pt-[max(1rem,env(safe-area-inset-top))] pb-[max(1.25rem,env(safe-area-inset-bottom))]"
    >
      {/* Обложка расплёскивается по верху экрана — здесь у неё больше всего места, чтобы задать тон. */}
      <span
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 bg-[linear-gradient(180deg,color-mix(in_srgb,var(--cover-tint)_55%,transparent),transparent_62%)]"
      />

      <header className="relative z-1 grid shrink-0 grid-cols-[1fr_auto_1fr] items-center gap-3">
        <Button
          variant="ghost"
          size="icon"
          className="size-11 justify-self-start"
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
              size="icon"
              className={cn("size-11", settings.dataSaver && "text-primary")}
              onClick={() => settings.update({ dataSaver: !settings.dataSaver })}
              aria-label={t("player.dataSaver")}
              aria-pressed={settings.dataSaver}
            >
              <DataSaverIcon size={20} />
            </Button>
          )}

          <Button
            variant="ghost"
            size="icon"
            className={cn("size-11", panel === "lyrics" && "text-primary")}
            onClick={() => setPanel((open) => (open === "lyrics" ? "art" : "lyrics"))}
            aria-label={panel === "lyrics" ? t("lyrics.hide") : t("lyrics.show")}
            aria-pressed={panel === "lyrics"}
            title={t("lyrics.title")}
          >
            <LyricsIcon size={20} />
          </Button>

          <Button
            variant="ghost"
            size="icon"
            className={cn("size-11", panel === "queue" && "text-primary")}
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
      ) : panel === "lyrics" ? (
        <div className="relative z-1 min-h-0 flex-1 overflow-y-auto px-3 py-5 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
          <LyricsPane key={track.id} track={track} />
        </div>
      ) : (
        <div className="relative z-1 mx-auto flex min-h-0 w-full max-w-[28.75rem] flex-1 flex-col justify-center gap-5">
          <div
            data-menu={menuOpen ? "open" : undefined}
            className="group relative aspect-square max-h-[44vh] w-full self-center overflow-hidden rounded-xl shadow-art"
          >
            <Cover
              albumId={track.albumId}
              trackId={track.id}
              hasCover={track.hasCover}
              name={track.albumTitle ?? track.title}
              size="100%"
              variant="full"
            />

            {/*
             * Управление лежит на самой обложке, но проявляется под курсором: картинка здесь
             * главное, и держать кнопки поверх неё постоянно значило бы её испортить. Наводить
             * нечем на сенсорном экране — там накладка видна всегда. Пока открыто меню «…»,
             * курсор уже за пределами обложки, и накладку удерживает data-menu: иначе меню
             * висело бы в воздухе, отделившись от кнопки, которая его вызвала.
             */}
            <div
              className={cn(
                "pointer-events-none absolute inset-0 flex flex-col p-3 opacity-0 transition-opacity duration-150 ease-brand",
                "bg-[linear-gradient(180deg,rgba(0,0,0,0.35),transparent_38%,rgba(0,0,0,0.5))]",
                "group-hover:pointer-events-auto group-hover:opacity-100",
                "group-focus-within:pointer-events-auto group-focus-within:opacity-100",
                "group-data-[menu=open]:pointer-events-auto group-data-[menu=open]:opacity-100",
                "[@media(pointer:coarse)]:pointer-events-auto [@media(pointer:coarse)]:opacity-100",
              )}
            >
              <div className="flex flex-1 items-center justify-center">{artTransport}</div>

              <div className="flex items-center justify-between">
                <TrackMenu
                  track={track}
                  open={menuOpen}
                  onOpenChange={setMenuOpen}
                  onChanged={() => invalidate("library", "playlists")}
                  onNavigate={onClose}
                  loadPlaylists={loadPlaylists}
                  onQueue={() => {
                    player.addToQueue(track);
                    notify(t("menu.addedToQueue", { title: track.title }), "success");
                    setMenuOpen(false);
                  }}
                  trigger={
                    <Button
                      variant="ghost"
                      size="icon"
                      className={cn(artButton, "text-white hover:text-white")}
                      aria-label={t("tracks.moreActions", { title: track.title })}
                    >
                      <MoreIcon size={20} />
                    </Button>
                  }
                />

                <Button
                  variant="ghost"
                  size="icon"
                  className={cn(
                    artButton,
                    track.isFavorite
                      ? "text-primary hover:text-primary"
                      : "text-white hover:text-white",
                  )}
                  onClick={onToggleFavorite}
                  aria-label={
                    track.isFavorite ? t("tracks.removeFromFavorites") : t("tracks.addToFavorites")
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

          <div className="flex flex-col gap-0.5">
            <Seekbar
              value={progress.position}
              max={duration}
              onSeek={player.seek}
              ariaLabel={t("player.seek")}
              commitOnRelease
            />
            <div className="flex justify-between text-xs text-muted-foreground tabular-nums">
              <span>{formatDuration(progress.position)}</span>
              <span>{formatDuration(duration)}</span>
            </div>
          </div>

          {transport}

          <div className="flex items-center justify-between gap-4">
            <Button
              variant="ghost"
              size="icon"
              className={cn("size-11", track.isFavorite && "text-primary")}
              onClick={onToggleFavorite}
              aria-label={
                track.isFavorite ? t("tracks.removeFromFavorites") : t("tracks.addToFavorites")
              }
            >
              <HeartIcon size={22} filled={track.isFavorite} />
            </Button>

            <div className="flex max-w-[12.5rem] flex-1 items-center gap-2">
              <Button
                variant="ghost"
                size="icon"
                className="size-11"
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
      )}
    </motion.div>
  );
}
