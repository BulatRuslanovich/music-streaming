"use client";

import { motion, useReducedMotion } from "motion/react";
import { cn } from "@/lib/cn";
import { formatArtists, formatDuration } from "@/lib/format";
import { usePlayer } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { DURATION, EASE } from "@/lib/motion";
import { Cover } from "./Cover";
import { Button } from "./ui/button";
import { CloseIcon, TrashIcon } from "./Icons";

export function QueuePanel({ onClose }: { onClose: () => void }) {
  const t = useT();
  const reduceMotion = useReducedMotion();

  return (
    <motion.aside
      aria-label={t("queue.label")}
      initial={reduceMotion ? false : { opacity: 0, y: 16, scale: 0.98 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, y: 16, scale: 0.98 }}
      transition={{ duration: DURATION * 1.5, ease: EASE }}
      className={cn(
        "fixed right-[1.125rem] bottom-[calc(var(--player-height)+1rem)] z-50 flex max-h-[min(60vh,32.5rem)] w-[min(22.5rem,calc(100vw-2.25rem))] flex-col rounded-xl border border-border-strong bg-popover/95 p-3.5 shadow-pop backdrop-blur-xl",
        "max-md:inset-x-3 max-md:bottom-[calc(var(--player-height)+env(safe-area-inset-bottom)+0.625rem)] max-md:max-h-[min(52dvh,26rem)] max-md:w-auto",
      )}
    >
      <header className="mb-2 flex items-center justify-between">
        <h3 className="font-bold">{t("queue.title")}</h3>
        <Button variant="ghost" size="icon" onClick={onClose} aria-label={t("queue.close")}>
          <CloseIcon size={18} />
        </Button>
      </header>

      <QueueList />
    </motion.aside>
  );
}

export function QueueList() {
  const player = usePlayer();
  const t = useT();

  if (player.queue.length === 0) {
    return <p className="py-8 text-muted-foreground">{t("queue.empty")}</p>;
  }

  const radioNote =
    player.radio === "loading"
      ? t("queue.radioLoading")
      : player.radio === "empty"
        ? t("queue.radioEmpty")
        : player.radio === "failed"
          ? t("queue.radioFailed")
          : null;

  return (
    <>
      <div className="mb-1.5 flex items-center justify-between border-b border-border px-0.5 pt-1 pb-2.5">
        <span className="text-sm text-muted-foreground">
          {t("count.tracks", { count: player.queue.length })}
        </span>
        <Button variant="text" size="auto" className="text-sm" onClick={player.clearQueue}>
          {t("action.clear")}
        </Button>
      </div>

      <ol className="flex flex-col gap-0.5 overflow-y-auto">
        {player.queue.map((track, index) => {
          const isCurrent = index === player.currentIndex;

          return (
            <li
              key={`${track.id}-${index}`}
              className="flex items-center gap-1 rounded-md hover:bg-accent"
            >
              <button
                type="button"
                onClick={() => player.jumpTo(index)}
                aria-current={isCurrent}
                className="flex min-w-0 flex-1 items-center gap-2.5 p-1.5 text-left"
              >
                <Cover
                  albumId={track.albumId}
                  trackId={track.id}
                  hasCover={track.hasCover}
                  name={track.albumTitle ?? track.title}
                  size={36}
                />
                <span className="flex min-w-0 flex-1 flex-col">
                  <span
                    className={cn("truncate text-sm font-semibold", isCurrent && "text-primary")}
                  >
                    {track.title}
                  </span>
                  <span className="truncate text-xs text-muted-foreground">
                    {formatArtists(track)}
                  </span>
                </span>
                <span className="text-xs text-muted-foreground tabular-nums">
                  {formatDuration(track.durationSeconds)}
                </span>
              </button>

              <Button
                variant="ghost"
                size="icon-sm"
                className="mr-1"
                onClick={() => player.removeFromQueue(index)}
                aria-label={t("queue.removeNamed", { title: track.title })}
              >
                <TrashIcon size={15} />
              </Button>
            </li>
          );
        })}
      </ol>

      {radioNote && (
        <p
          role="status"
          className={cn(
            "p-3 text-center text-sm",
            player.radio === "loading" ? "text-primary" : "text-muted-foreground",
          )}
        >
          {radioNote}
        </p>
      )}
    </>
  );
}
