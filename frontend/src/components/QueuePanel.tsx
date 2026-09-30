// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import dynamic from "next/dynamic";
import { type DragEndEvent } from "@dnd-kit/core";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import { cn } from "@/lib/cn";
import { formatArtists, formatDuration } from "@/lib/format";
import type { QueueSignals, RecommendationReason, Track } from "@/lib/types";
import { usePlayer } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { useInvalidate } from "@/lib/useInvalidate";
import { useToast } from "@/lib/useToast";
import { reasonLabel } from "@/lib/recommendationReason";
import { TrackCover } from "./Cover";
import { EmptyState } from "./EmptyState";
import { Button } from "./ui/button";
import { DragHandle, VerticalSortable } from "./VerticalSortable";
import { ListMusicIcon, ListVideoIcon, Trash2Icon, XIcon } from "lucide-react";

const PlaylistDialog = dynamic(() => import("./PlaylistDialog").then((m) => m.PlaylistDialog));

const SORTABLE_PREFIX = "queue-";

export function QueuePanel({ onClose }: { onClose: () => void }) {
  const t = useT();

  return (
    <aside
      aria-label={t("queue.label")}
      className={cn(
        "fixed right-4 bottom-[calc(var(--player-height)+0.75rem)] z-50 flex max-h-[min(60vh,32.5rem)] w-[min(22.5rem,calc(100vw-2rem))] flex-col rounded-lg bg-popover p-3.5 shadow-pop",
        "animate-in duration-200 fade-in-0 slide-in-from-bottom-3",
        "max-md:inset-x-3 max-md:bottom-[calc(var(--player-height)+var(--mobile-nav-height)+env(safe-area-inset-bottom)+0.625rem)] max-md:max-h-[min(52dvh,26rem)] max-md:w-auto",
      )}
    >
      <header className="mb-2 flex items-center justify-between">
        <h3 className="text-section font-semibold">{t("queue.title")}</h3>
        <Button variant="ghost" size="icon" onClick={onClose} aria-label={t("queue.close")}>
          <XIcon size={16} />
        </Button>
      </header>

      <QueueList />
    </aside>
  );
}

export function QueueList() {
  const player = usePlayer();
  const t = useT();
  const { notify } = useToast();
  const invalidate = useInvalidate();
  const listRef = useRef<HTMLOListElement>(null);

  useEffect(() => {
    listRef.current?.querySelector("[data-current]")?.scrollIntoView({ block: "center" });
  }, []);

  if (player.queue.length === 0) {
    return <EmptyState bare icon={<ListVideoIcon size={24} />} title={t("queue.empty")} />;
  }

  const radioNote =
    player.radio === "loading"
      ? t("queue.radioLoading")
      : player.radio === "empty"
        ? t("queue.radioEmpty")
        : player.radio === "failed"
          ? t("queue.radioFailed")
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

  /**
   * Одним запросом: сервер вставляет всю очередь атомарно и в её порядке. Раньше это было по
   * запросу на трек, строго последовательно, и обрыв посередине оставлял полплейлиста.
   * Ошибку показывает форма создания, внутри которой это и вызывается.
   */
  const saveAsPlaylist = async (playlistId: string) => {
    try {
      await api.addToPlaylist(
        playlistId,
        player.queue.map((track) => track.id),
      );
    } finally {
      // Плейлист уже создан, даже если треки в него не легли, — список плейлистов надо обновить.
      invalidate("playlists");
    }
  };

  return (
    <>
      <div className="mb-1.5 flex items-center justify-between gap-2 px-0.5 pt-1 pb-2.5">
        <span className="min-w-0 truncate text-sm text-muted-foreground">
          {t("count.tracks", { count: player.queue.length })}
        </span>

        <div className="flex items-center gap-3">
          <SaveQueueButton onSave={saveAsPlaylist} />

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
                  ? player.radioSession?.reasons[track.id]
                  : undefined
              }
              signals={player.radioSession?.signals?.[track.id]}
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
            player.radio === "loading" ? "text-primary" : "text-muted-foreground",
          )}
        >
          {radioNote}
        </p>
      )}
    </>
  );
}

// Отдельного «сохраняем» у кнопки нет: диалог ждёт onSave и закрывается только после него.
function SaveQueueButton({ onSave }: { onSave: (playlistId: string) => Promise<void> }) {
  const t = useT();
  const [open, setOpen] = useState(false);

  return (
    <>
      <Button
        variant="ghost"
        size="icon-sm"
        onClick={() => setOpen(true)}
        aria-label={t("queue.saveAsPlaylist")}
        title={t("queue.saveAsPlaylist")}
      >
        <ListMusicIcon size={16} />
      </Button>

      {open && (
        <PlaylistDialog
          onClose={() => setOpen(false)}
          afterCreate={onSave}
          successMessage={t("queue.savedAsPlaylist")}
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
  signals,
  onPlay,
  onRemove,
}: {
  track: Track;
  index: number;
  isCurrent: boolean;
  startsUpNext: boolean;
  reason?: RecommendationReason;
  signals?: QueueSignals;
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
        <li aria-hidden="true" className="px-1.5 pt-2 pb-1 text-xs text-muted-foreground">
          {t("queue.upNext")}
        </li>
      )}

      <li
        ref={setNodeRef}
        data-current={isCurrent ? "true" : undefined}
        style={{ transform: CSS.Transform.toString(transform), transition }}
        className={cn(
          "group flex items-center gap-1 rounded-sm hover:bg-accent",
          isDragging && "z-10 opacity-90 shadow-pop",
        )}
      >
        <DragHandle
          {...attributes}
          {...listeners}
          aria-label={t("tracks.reorderNamed", { title: track.title })}
          className="ml-1"
        />

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
              <span className="flex min-w-0 items-center gap-1.5 text-2xs text-faint">
                {signals?.explore && (
                  <span className="shrink-0 rounded-full bg-raised px-1.5 py-px font-medium text-primary">
                    {t("queue.explore")}
                  </span>
                )}
                <span className="truncate">{reasonLabel(reason, t)}</span>
              </span>
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
          <Trash2Icon size={16} />
        </Button>
      </li>
    </>
  );
}
