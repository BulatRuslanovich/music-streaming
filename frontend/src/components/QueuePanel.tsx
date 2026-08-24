// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import dynamic from "next/dynamic";
import { type DragEndEvent } from "@dnd-kit/core";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { motion, useReducedMotion } from "motion/react";
import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import { cn } from "@/lib/cn";
import { formatArtists, formatDuration } from "@/lib/format";
import type { DjMode, DjVariety, RecommendationReason, Track } from "@/lib/types";
import { usePlayer } from "@/contexts/PlayerContext";
import { useSleepTimer } from "@/contexts/SleepTimerContext";
import { useT } from "@/contexts/I18nContext";
import { useInvalidate } from "@/lib/useInvalidate";
import { useToast } from "@/contexts/ToastContext";
import { DURATION, EASE } from "@/lib/motion";
import { TrackCover } from "./Cover";
import { Button } from "./ui/button";
import { ToggleGroup, ToggleGroupButton } from "./ui/tabs";
import { VerticalSortable } from "./VerticalSortable";
import { CloseIcon, GripIcon, PlaylistIcon, TrashIcon } from "./Icons";

const CreatePlaylistDialog = dynamic(() =>
  import("./CreatePlaylistDialog").then((m) => m.CreatePlaylistDialog),
);

const SORTABLE_PREFIX = "queue-";

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
        "max-md:inset-x-3 max-md:bottom-[calc(var(--player-height)+var(--mobile-nav-height)+env(safe-area-inset-bottom)+0.625rem)] max-md:max-h-[min(52dvh,26rem)] max-md:w-auto",
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
  const { notify } = useToast();
  const sleep = useSleepTimer();
  const invalidate = useInvalidate();
  const [saving, setSaving] = useState(false);
  const listRef = useRef<HTMLOListElement>(null);

  useEffect(() => {
    listRef.current?.querySelector("[data-current]")?.scrollIntoView({ block: "center" });
  }, []);

  if (player.queue.length === 0) {
    return <p className="py-8 text-muted-foreground">{t("queue.empty")}</p>;
  }

  const continuation = player.dj?.status ?? player.radio;
  const radioNote =
    continuation === "loading"
      ? t("queue.radioLoading")
      : continuation === "empty"
        ? t(player.dj ? "dj.finished" : "queue.radioEmpty")
        : continuation === "failed"
          ? t(player.dj ? "dj.continueFailed" : "queue.radioFailed")
          : null;

  const undoable = (message: string, snapshot: ReturnType<typeof player.snapshotQueue>) => {
    notify(message, "info", { label: t("action.undo"), run: () => player.restoreQueue(snapshot) });
  };

  const onDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over || active.id === over.id) return;

    const from = Number(String(active.id).slice(SORTABLE_PREFIX.length));
    const to = Number(String(over.id).slice(SORTABLE_PREFIX.length));
    if (Number.isNaN(from) || Number.isNaN(to)) return;

    player.moveInQueue(from, to);
  };

  const saveAsPlaylist = async (playlistId: string) => {
    setSaving(true);
    try {
      for (const track of player.queue) await api.addToPlaylist(playlistId, track.id);
      invalidate("playlists");
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      {player.dj && (
        <DjControls
          mode={player.dj.mode}
          variety={player.dj.variety}
          onChange={player.setDjVariety}
        />
      )}

      <div className="mb-1.5 flex items-center justify-between gap-2 border-b border-border px-0.5 pt-1 pb-2.5">
        <span className="min-w-0 truncate text-sm text-muted-foreground">
          {sleep.plan.kind === "track"
            ? t("sleep.remainingTrack")
            : sleep.plan.kind === "timer"
              ? t("sleep.remaining", { minutes: sleep.minutesLeft ?? 1 })
              : t("count.tracks", { count: player.queue.length })}
        </span>

        <div className="flex items-center gap-3">
          <SaveQueueButton pending={saving} onSave={saveAsPlaylist} />

          <Button
            variant="text"
            size="auto"
            className="text-sm"
            onClick={() => {
              const snapshot = player.snapshotQueue();
              player.clearQueue();
              undoable(t("queue.cleared"), snapshot);
            }}
          >
            {t("action.clear")}
          </Button>
        </div>
      </div>

      <VerticalSortable
        items={player.queue.map((_, index) => `${SORTABLE_PREFIX}${index}`)}
        onDragEnd={onDragEnd}
      >
        <ol ref={listRef} className="flex flex-col gap-0.5 overflow-y-auto">
          {player.queue.map((track, index) => (
            <QueueRow
              key={`${track.id}-${index}`}
              track={track}
              index={index}
              isCurrent={index === player.currentIndex}
              startsUpNext={index === player.currentIndex + 1 && player.currentIndex >= 0}
              reason={
                index === player.currentIndex || index === player.currentIndex + 1
                  ? player.dj?.reasons[track.id]
                  : undefined
              }
              onPlay={() => player.jumpTo(index)}
              onRemove={() => {
                const snapshot = player.snapshotQueue();
                player.removeFromQueue(index);
                undoable(t("queue.removed", { title: track.title }), snapshot);
              }}
            />
          ))}
        </ol>
      </VerticalSortable>

      {radioNote && (
        <p
          role="status"
          className={cn(
            "p-3 text-center text-sm",
            continuation === "loading" ? "text-primary" : "text-muted-foreground",
          )}
        >
          {radioNote}
        </p>
      )}
    </>
  );
}

const VARIETIES: DjVariety[] = ["Familiar", "Balanced", "Adventurous"];

function DjControls({
  mode,
  variety,
  onChange,
}: {
  mode: DjMode;
  variety: DjVariety;
  onChange: (value: DjVariety) => void;
}) {
  const t = useT();

  return (
    <div className="mb-2 border-y border-border py-1.5">
      <div className="flex min-w-0 items-center gap-2 px-1 pb-1">
        <strong className="shrink-0 text-xs tracking-wide uppercase">Caimack DJ</strong>
        <span className="truncate text-xs text-muted-foreground">{t(`dj.mode.${mode}`)}</span>
      </div>
      <ToggleGroup
        variant="underline"
        className="grid grid-cols-3"
        aria-label={t("dj.varietyLabel")}
      >
        {VARIETIES.map((value) => (
          <ToggleGroupButton
            key={value}
            variant="underline"
            active={variety === value}
            onClick={() => onChange(value)}
            className="justify-center px-1 py-1.5 text-xs"
          >
            {t(`dj.variety.${value}`)}
          </ToggleGroupButton>
        ))}
      </ToggleGroup>
    </div>
  );
}

function SaveQueueButton({
  pending,
  onSave,
}: {
  pending: boolean;
  onSave: (playlistId: string) => Promise<void>;
}) {
  const t = useT();
  const { notify } = useToast();
  const [open, setOpen] = useState(false);

  return (
    <>
      <Button
        variant="ghost"
        size="icon-sm"
        disabled={pending}
        onClick={() => setOpen(true)}
        aria-label={t("queue.saveAsPlaylist")}
        title={t("queue.saveAsPlaylist")}
      >
        <PlaylistIcon size={16} />
      </Button>

      {open && (
        <CreatePlaylistDialog
          onClose={() => setOpen(false)}
          afterCreate={onSave}
          onCreated={() => notify(t("queue.savedAsPlaylist"), "success")}
        />
      )}
    </>
  );
}

function QueueRow({
  track,
  index,
  isCurrent,
  startsUpNext,
  reason,
  onPlay,
  onRemove,
}: {
  track: Track;
  index: number;
  isCurrent: boolean;
  startsUpNext: boolean;
  reason?: RecommendationReason;
  onPlay: () => void;
  onRemove: () => void;
}) {
  const t = useT();
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: `${SORTABLE_PREFIX}${index}`,
  });

  return (
    <>
      {startsUpNext && (
        <li aria-hidden="true" className="px-1.5 pt-2 pb-1 text-xs text-faint uppercase">
          {t("queue.upNext")}
        </li>
      )}

      <li
        ref={setNodeRef}
        data-current={isCurrent ? "true" : undefined}
        style={{ transform: CSS.Transform.toString(transform), transition }}
        className={cn(
          "group flex items-center gap-1 rounded-md hover:bg-accent",
          isDragging && "z-10 opacity-90 shadow-pop",
        )}
      >
        <button
          type="button"
          {...attributes}
          {...listeners}
          aria-label={t("tracks.reorderNamed", { title: track.title })}
          className="ml-1 cursor-grab text-faint opacity-0 transition-opacity group-hover:opacity-100 focus-visible:opacity-100 active:cursor-grabbing max-md:opacity-100"
        >
          <GripIcon size={14} />
        </button>

        <button
          type="button"
          onClick={onPlay}
          aria-current={isCurrent}
          className="flex min-w-0 flex-1 items-center gap-2.5 p-1.5 text-left"
        >
          <TrackCover track={track} size={36} />
          <span className="flex min-w-0 flex-1 flex-col">
            <span className={cn("truncate text-sm font-semibold", isCurrent && "text-primary")}>
              {track.title}
            </span>
            <span className="truncate text-xs text-muted-foreground">{formatArtists(track)}</span>
            {reason && (
              <span className="truncate text-2xs text-faint">{reasonLabel(reason, t)}</span>
            )}
          </span>
          <span className="text-xs text-muted-foreground tabular-nums">
            {formatDuration(track.durationSeconds)}
          </span>
        </button>

        <Button
          variant="ghost"
          size="icon-sm"
          className="mr-1"
          onClick={onRemove}
          aria-label={t("queue.removeNamed", { title: track.title })}
        >
          <TrashIcon size={15} />
        </Button>
      </li>
    </>
  );
}

function reasonLabel(reason: RecommendationReason, t: ReturnType<typeof useT>): string {
  const subject = reason.subject ?? "";

  switch (reason.kind) {
    case "becauseYouListened":
      return t("rec.reason.becauseYouListened", { subject });
    case "similarTo":
      return t("rec.reason.similarTo", { subject });
    case "popularWithSimilarTaste":
      return t("rec.reason.similarTaste");
    case "newFromArtistYouPlay":
      return t("rec.reason.newFromArtist", { subject });
    case "fromGenreYouLike":
      return t("rec.reason.genre", { subject });
    case "trending":
      return t("rec.reason.trending");
    case "freshInLibrary":
      return t("rec.reason.fresh");
    case "continueListening":
      return t("rec.reason.continueListening");
    case "rediscovery":
      return t("rec.reason.rediscovery");
    default:
      return t("rec.reason.discovery");
  }
}
