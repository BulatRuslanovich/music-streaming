// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { AnimatePresence } from "motion/react";
import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/cn";
import { trackCoverUrl } from "@/lib/media";
import { formatDuration } from "@/lib/format";
import { useCoverAccent } from "@/lib/useCoverAccent";
import { useCoverPalette } from "@/lib/useCoverColor";
import { resolveShortcut, shortcutNeedsTrack } from "@/lib/shortcuts";
import { usePlaybackProgress } from "@/lib/usePlaybackProgress";
import { useToggleFavorite } from "@/lib/useToggleFavorite";
import { useWindowKeyDown } from "@/lib/useWindowKeyDown";
import { usePlayerActions, usePlayerState } from "@/contexts/PlayerContext";
import { useSettings } from "@/contexts/SettingsContext";
import { useT } from "@/contexts/I18nContext";
import { ArtistLinks } from "./ArtistLinks";
import { TrackCover } from "./Cover";
import { Seekbar } from "./Seekbar";
import { PlayerTransport } from "./PlayerTransport";
import { PlayerVolume } from "./PlayerVolume";
import { DataSaverToggle } from "./DataSaverToggle";
import { Spectrum } from "./Spectrum";
import { useCagePerformance } from "@/lib/useCagePerformance";
import { FullScreenPlayer } from "./FullScreenPlayer";
import { QueuePanel } from "./QueuePanel";
import { Button } from "./ui/button";
import { ChevronUpIcon, HeartIcon, NextIcon, PauseIcon, PlayIcon, QueueIcon } from "./Icons";

const VOLUME_STEP = 0.05;

// Тот же радиус, что у контентной панели: на десктопе плеер — отдельная панель в общем
// жёлобе, а не приклеенная к низу полоса. На телефоне жёлоба нет, панель идёт от края
// до края, и скругление там не к чему прижаться.
const shellClass =
  "relative min-h-(--player-height) overflow-hidden rounded-xl bg-canvas px-5 py-2.5 [grid-area:player] max-md:rounded-none max-md:px-2.5 max-md:pt-2 max-md:pb-1";

/**
 * Цвет играющей обложки в самом плеере. `--cover-tint` уже считается для `TintScrim`,
 * но доставался только области контента: приложение окрашивалось, а плеер оставался
 * плоским чёрным при любом треке. Тот же переход в 700ms, что и у подложки страницы,
 * поэтому смена трека читается как одно движение, а не как два независимых.
 */
function PlayerTint() {
  return (
    <span
      aria-hidden="true"
      className={cn(
        "pointer-events-none absolute inset-0 z-0",
        "[transition:--cover-tint_700ms_var(--ease),--cover-tint-2_700ms_var(--ease)]",
        "bg-[linear-gradient(100deg,var(--tint),var(--tint-2)_38%,transparent_72%)]",
        "opacity-(--veil-player)",
      )}
    />
  );
}

/**
 * Полоса перемотки со временем по краям — единственное, чему нужен контекст прогресса, и
 * поэтому единственное, что перерисовывается по его тику (четыре раза в секунду).
 * `fallbackDuration` покрывает окно до `loadedmetadata`, когда декодированной длительности
 * ещё нет, а в метаданных трека она уже есть.
 *
 * Один компонент на оба места. Раньше их было два почти одинаковых: `MobileProgress` уже
 * рисовал время по краям полосы, а на десктопе полоса была волоском по кромке футера, и
 * часы жили отдельно справа слитной строкой «1:23 / 4:56». Различие осталось одно — на
 * десктопе есть подпись под курсором, на тач-экране она бессмысленна.
 */
function ProgressRow({
  fallbackDuration,
  tooltip = false,
  className,
}: {
  fallbackDuration: number;
  tooltip?: boolean;
  className?: string;
}) {
  const progress = usePlaybackProgress(fallbackDuration);

  const clock = "w-10 shrink-0 text-2xs text-faint tabular-nums";

  return (
    <div className={cn("flex w-full items-center gap-2", className)}>
      <span className={cn(clock, "text-right")}>{formatDuration(progress.position)}</span>

      <Seekbar
        className="min-w-0 flex-1"
        variant="player"
        value={progress.position}
        max={progress.total}
        onSeek={progress.seek}
        ariaLabel={progress.seekLabel}
        style={{ ["--buffered" as string]: `${progress.bufferedPercent}%` }}
        tooltip={tooltip ? formatDuration : undefined}
        commitOnRelease
      />

      <button
        type="button"
        onClick={progress.toggleRemainingTime}
        aria-label={progress.toggleRemainingLabel}
        title={progress.toggleRemainingLabel}
        className={cn(clock, "rounded-sm text-left hover:text-foreground")}
      >
        {progress.endLabel}
      </button>
    </div>
  );
}

export function Player({ onOverlay }: { onOverlay: (overlay: "palette" | "shortcuts") => void }) {
  // INFO: прогресс сюда сознательно не подписан — он тикает 4 раза в секунду и утащил бы
  // за собой очередь и полноэкранный плеер. Его читают только PlayerSeek и PlayerTime.
  const state = usePlayerState();
  const actions = usePlayerActions();
  const player = { ...state, ...actions };
  const settings = useSettings();
  const t = useT();

  const [expanded, setExpanded] = useState(false);
  const [queueOpen, setQueueOpen] = useState(false);
  const volumeRef = useRef<HTMLDivElement>(null);
  const { currentTrack } = state;

  // Отсчёт 4′33″ живёт здесь, а не в полноэкранном плеере: тот смонтирован только пока открыт,
  // а пауза, после которой уходят от компьютера, случается на обычной панели.
  const cage = useCagePerformance(Boolean(currentTrack) && !state.isPlaying);

  const coverUrl = trackCoverUrl(currentTrack, "thumb");

  const palette = useCoverPalette(coverUrl);

  useCoverAccent(palette.tint, palette.tintAlt);

  useEffect(() => {
    const element = volumeRef.current;
    if (!element) return;

    const onWheel = (event: WheelEvent) => {
      if (event.deltaY === 0) return;

      event.preventDefault();
      const current = state.muted ? 0 : state.volume;
      actions.setVolume(current + (event.deltaY < 0 ? VOLUME_STEP : -VOLUME_STEP));
    };

    element.addEventListener("wheel", onWheel, { passive: false });
    return () => element.removeEventListener("wheel", onWheel);
  }, [state.muted, state.volume, actions]);

  const toggleFavorite = useToggleFavorite();
  const likeCurrent = () => {
    if (currentTrack) void toggleFavorite(currentTrack);
  };

  useWindowKeyDown((event) => {
    const target = event.target as HTMLElement | null;
    const isTyping =
      target?.tagName === "INPUT" ||
      target?.tagName === "TEXTAREA" ||
      target?.isContentEditable === true;

    if (isTyping) return;

    const hit = resolveShortcut(event);
    if (!hit) return;

    // Полноэкранный плеер — тоже диалог, но это сам плеер, и клавиши в нём должны работать.
    const inOverlay =
      document.querySelector(
        "[data-state='open'][role='dialog']:not([data-player-fullscreen]), [data-state='open'][role='menu']",
      ) !== null;
    if (inOverlay && hit.action !== "palette" && hit.action !== "help") return;

    if (!currentTrack && shortcutNeedsTrack(hit.action)) return;

    event.preventDefault();

    switch (hit.action) {
      case "playPause":
        actions.toggle();
        break;
      case "seekBy":
        actions.seekBy(hit.value ?? 0);
        break;
      case "seekPercent": {
        // Длину берём функцией, а не из контекста прогресса: подписка на него стоила бы
        // плееру перерисовки на каждый тик ради одной цифры, нужной раз в нажатие.
        const total = actions.getDuration() || currentTrack?.durationSeconds || 0;
        actions.seek((total * (hit.value ?? 0)) / 100);
        break;
      }
      case "next":
        actions.next();
        break;
      case "previous":
        actions.previous();
        break;
      case "volumeBy":
        actions.setVolume((state.muted ? 0 : state.volume) + (hit.value ?? 0));
        break;
      case "mute":
        actions.toggleMute();
        break;
      case "favorite":
        likeCurrent();
        break;
      case "shuffle":
        actions.toggleShuffle();
        break;
      case "repeat":
        actions.cycleRepeat();
        break;
      case "queue":
        setQueueOpen((open) => !open);
        break;
      case "palette":
        onOverlay("palette");
        break;
      case "help":
        onOverlay("shortcuts");
        break;
    }
  });

  if (!currentTrack) {
    return (
      <footer className={cn(shellClass, "grid place-items-center")}>
        <p className="text-muted-foreground">{t("player.idle")}</p>
      </footer>
    );
  }

  return (
    <>
      <footer className={shellClass}>
        <PlayerTint />

        {/* `h-auto` на телефоне обязателен: с `h-full` эта строка забирала всю высоту футера,
            и полоса со временем под ней уходила под `overflow-hidden`. */}
        {/* Центральная колонка ограничена сверху: с `auto` она росла по содержимому, а теперь
            в ней две строки, и полоса перемотки растянулась бы на всю свободную ширину. */}
        <div className="relative z-1 grid h-full grid-cols-[minmax(0,1fr)_minmax(0,28rem)_minmax(0,1fr)] items-center gap-5 max-md:h-auto max-md:grid-cols-1 max-md:gap-0">
          <div className="flex min-w-0 items-center gap-3 max-md:gap-2.5">
            <button
              type="button"
              onClick={() => setExpanded(true)}
              aria-label={t("player.openFull")}
              className="rounded-md leading-none shadow-art"
            >
              <TrackCover track={currentTrack} size="var(--player-cover)" />
            </button>

            <div className="flex min-w-0 flex-col">
              {/* Название ведёт на альбом: раньше клик по нему проваливался на полосу
                  перемотки, растянутую на весь футер, и сбивал позицию в треке. */}
              {currentTrack.albumId ? (
                <Link
                  href={`/albums/${currentTrack.albumId}`}
                  className="truncate font-semibold hover:underline"
                >
                  {currentTrack.title}
                </Link>
              ) : (
                <span className="truncate font-semibold">{currentTrack.title}</span>
              )}
              <ArtistLinks
                track={currentTrack}
                className="truncate text-sm text-muted-foreground"
              />
            </div>

            <Button
              variant="ghost"
              size="icon"
              className={cn("max-md:hidden", currentTrack.isFavorite && "text-primary")}
              onClick={likeCurrent}
              aria-label={
                currentTrack.isFavorite
                  ? t("tracks.removeFromFavorites")
                  : t("tracks.addToFavorites")
              }
              aria-pressed={currentTrack.isFavorite}
            >
              <HeartIcon size={20} filled={currentTrack.isFavorite} />
            </Button>

            <div className="md:hidden ml-auto flex items-center gap-0.5">
              <Button
                variant="ghost"
                size="icon"
                onClick={player.toggle}
                aria-label={player.isPlaying ? t("action.pause") : t("action.play")}
              >
                {player.isPlaying ? <PauseIcon size={24} /> : <PlayIcon size={24} />}
              </Button>

              {/* Пропуск — вторая по частоте операция после паузы, а до этого он был
                  доступен только из полноэкранного плеера или системных медиа-кнопок. */}
              <Button
                variant="ghost"
                size="icon"
                onClick={player.next}
                aria-label={t("player.nextTrack")}
              >
                <NextIcon size={24} />
              </Button>

              <Button
                variant="ghost"
                size="icon"
                onClick={() => setExpanded(true)}
                aria-label={t("player.openFull")}
              >
                <ChevronUpIcon size={20} />
              </Button>
            </div>
          </div>

          <div className="max-md:hidden relative flex min-w-0 flex-col items-center gap-1">
            {/*
              Спектр компактный и центрованный, а не полосой во всю панель: растянутый по
              футеру он ложился на время, на имя исполнителя и на «Дальше» — фактура
              превращалась в помеху. Здесь его ось симметрии совпадает с центром транспорта.

              `bottom-7` выводит столбики из-под строки со временем: цифры мелкие, и читать
              их поверх пляшущих делений невозможно.

              Отрицательный слой работает только благодаря `z-1` на сетке выше: она создаёт
              стекающий контекст. Будь спектр прямым потомком футера (у того `relative` без
              `z-index`), `-z-10` увёл бы его за собственный `bg-canvas` футера — и спектр
              пропал бы совсем.
            */}
            <Spectrum
              className={cn(
                "pointer-events-none absolute inset-x-0 bottom-7 -z-10 h-9 opacity-60",
                // Растушёвка только у самой кромки. Градиент от самого низа гасил верхушки
                // столбиков — ровно ту часть, по которой видна разница высот, — и спектр
                // читался как ровная плита.
                "[mask-image:linear-gradient(to_top,#000_82%,transparent)]",
              )}
            />

            <PlayerTransport />
            <ProgressRow tooltip fallbackDuration={currentTrack.durationSeconds} />
          </div>

          <div className="max-md:hidden flex min-w-0 items-center justify-end gap-1.5">
            {state.nextTrack && (
              <button
                type="button"
                onClick={() => setQueueOpen(true)}
                title={t("player.upNextNamed", { title: state.nextTrack.title })}
                className={cn(
                  "mr-1 flex min-w-0 max-w-44 items-center gap-2 rounded-md px-1.5 py-1 text-left",
                  "transition-colors duration-150 ease-brand hover:bg-accent",
                  // Ниже этой ширины в правой колонке уже не остаётся места на громкость.
                  // Порог ниже прежних 1340: в полосе 900–1280 сайдбар свёрнут сам,
                  // и футеру достались те самые 160 пикселей.
                  "max-[1180px]:hidden",
                )}
              >
                <TrackCover track={state.nextTrack} size={26} />
                <span className="flex min-w-0 flex-col leading-tight">
                  <span className="text-2xs text-faint uppercase">{t("player.upNext")}</span>
                  <span className="truncate text-xs text-muted-foreground">
                    {state.nextTrack.title}
                  </span>
                </span>
              </button>
            )}

            <DataSaverToggle
              // Тише остальных в покое: это переключатель на весь сеанс, а не то, чем
              // пользуются в каждом треке. Включённым он говорит акцентом в полный голос.
              className={cn(
                "text-faint hover:text-foreground max-xl:hidden",
                settings.dataSaver && "hover:text-primary",
              )}
              withTitle
            />

            <Button
              variant="ghost"
              size="icon"
              className={cn(queueOpen && "text-primary")}
              onClick={() => setQueueOpen((open) => !open)}
              aria-label={t("queue.label")}
              aria-pressed={queueOpen}
              title={t("queue.title")}
            >
              <QueueIcon size={20} />
            </Button>

            <div ref={volumeRef} className="flex items-center gap-1.5">
              <PlayerVolume seekbarClassName="max-w-[7.5rem]" />
            </div>
          </div>
        </div>

        <div className="md:hidden relative z-1">
          <ProgressRow fallbackDuration={currentTrack.durationSeconds} />
        </div>
      </footer>

      <AnimatePresence>
        {queueOpen && <QueuePanel key="queue" onClose={() => setQueueOpen(false)} />}
      </AnimatePresence>

      <AnimatePresence>
        {expanded && (
          <FullScreenPlayer
            key="fullscreen"
            cage={cage}
            onClose={() => setExpanded(false)}
            onToggleFavorite={likeCurrent}
          />
        )}
      </AnimatePresence>
    </>
  );
}
