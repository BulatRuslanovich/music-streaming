"use client";

import {
  DndContext,
  KeyboardSensor,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import { restrictToParentElement, restrictToVerticalAxis } from "@dnd-kit/modifiers";
import {
  SortableContext,
  arrayMove,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import Link from "next/link";
import { useCallback, useRef, useState } from "react";
import { api } from "@/lib/api";
import { cn } from "@/lib/cn";
import { recordEvent } from "@/lib/events";
import { formatDuration } from "@/lib/format";
import { useFormat } from "@/lib/useFormat";
import { useInvalidate } from "@/lib/useInvalidate";
import type { Playlist, Track } from "@/lib/types";
import { usePlayer, type PlaybackOrigin } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { ArtistLinks } from "./ArtistLinks";
import { Cover } from "./Cover";
import { EmptyState } from "./EmptyState";
import { TrackMenu } from "./TrackMenu";
import { Button, PressButton } from "./ui/button";
import { Overline } from "./ui/label";
import { GripIcon, HeartIcon, PauseIcon, PlayIcon } from "./Icons";

interface TrackListProps {
  tracks: Track[];
  showCover?: boolean;
  showAlbum?: boolean;
  showArtist?: boolean;
  useTrackNumbers?: boolean;
  playedAt?: Record<string, string>;
  onChanged?: () => void;
  playlistId?: string;
  onReorder?: (trackIds: string[]) => void;
  emptyMessage?: string;
  origin?: PlaybackOrigin;
}

/*
 * Раскладка строки. Число колонок зависит от того, показаны ли альбом и дата прослушивания:
 * лишняя ячейка при нехватке колонок уезжает на вторую строку и ломает всю таблицу.
 *
 * Варианты перечислены целиком, а не собраны из кусков: Tailwind ищет имена классов в
 * исходнике текстом и склеенного во время работы попросту не увидит.
 */
const ROW_COLUMNS = {
  "album+date": "grid-cols-[2.75rem_minmax(0,3fr)_minmax(0,2fr)_7rem_4.75rem_5.5rem]",
  album: "grid-cols-[2.75rem_minmax(0,3fr)_minmax(0,2fr)_4.75rem_5.5rem]",
  date: "grid-cols-[2.75rem_minmax(0,3fr)_7rem_4.75rem_5.5rem]",
  plain: "grid-cols-[2.75rem_minmax(0,1fr)_4.75rem_5.5rem]",
} as const;

/* На узком экране альбом и дата скрыты, поэтому колонок всегда четыре, а ниже 380px — три. */
const rowBase =
  "grid items-center gap-3 rounded-md px-2.5 py-2 max-md:grid-cols-[2.125rem_minmax(0,1fr)_auto_auto] max-md:gap-2 max-md:px-1 max-[380px]:grid-cols-[2.125rem_minmax(0,1fr)_auto]";

function rowGridFor(showAlbum: boolean, showDate: boolean): string {
  const key =
    showAlbum && showDate ? "album+date" : showAlbum ? "album" : showDate ? "date" : "plain";
  return cn(rowBase, ROW_COLUMNS[key]);
}

export function TrackList({
  tracks,
  showCover = true,
  showAlbum = true,
  showArtist = true,
  useTrackNumbers = false,
  playedAt,
  onChanged,
  playlistId,
  onReorder,
  emptyMessage,
  origin,
}: TrackListProps) {
  const player = usePlayer();
  const { notify, notifyError } = useToast();
  const invalidate = useInvalidate();
  const t = useT();

  const [menuFor, setMenuFor] = useState<string | null>(null);
  const [favorites, setFavorites] = useState<Record<string, boolean>>({});

  const [renderedTracks, setRenderedTracks] = useState(tracks);
  if (tracks !== renderedTracks) {
    setRenderedTracks(tracks);
    setFavorites({});
  }

  const playlistsRequest = useRef<Promise<Playlist[]> | null>(null);
  const loadPlaylists = useCallback(() => {
    playlistsRequest.current ??= api.playlists().catch(() => []);
    return playlistsRequest.current;
  }, []);

  const changed = useCallback(() => {
    invalidate("library", "playlists");
    onChanged?.();
  }, [invalidate, onChanged]);

  const isFavorite = useCallback(
    (track: Track) => favorites[track.id] ?? track.isFavorite,
    [favorites],
  );

  const toggleFavorite = useCallback(
    async (track: Track) => {
      const next = !isFavorite(track);

      setFavorites((current) => ({ ...current, [track.id]: next }));
      player.patchTrack(track.id, { isFavorite: next });

      try {
        if (next) await api.addFavorite(track.id);
        else await api.removeFavorite(track.id);

        recordEvent({ type: next ? "trackLiked" : "trackUnliked", trackId: track.id });
        invalidate("favorites");
      } catch (error) {
        setFavorites((current) => ({ ...current, [track.id]: !next }));
        player.patchTrack(track.id, { isFavorite: !next });
        notifyError(error, t("tracks.favoritesFailed"));
      }
    },
    [isFavorite, invalidate, notifyError, player, t],
  );

  const play = useCallback(
    (index: number) => {
      const track = tracks[index];
      if (player.currentTrack?.id === track.id) {
        player.toggle();
        return;
      }
      player.playQueue(tracks, index, origin);
    },
    [player, tracks, origin],
  );

  /*
   * Перетаскивание начинается после нескольких пикселей движения: без этого простое нажатие
   * на строку считалось бы началом перетаскивания и трек не запускался бы.
   */
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  const sortable = Boolean(playlistId && onReorder);

  const onDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over || active.id === over.id || !onReorder) return;

    const from = tracks.findIndex((track) => track.id === active.id);
    const to = tracks.findIndex((track) => track.id === over.id);
    if (from < 0 || to < 0) return;

    onReorder(arrayMove(tracks, from, to).map((track) => track.id));
  };

  if (tracks.length === 0) {
    return <EmptyState title={emptyMessage ?? t("tracks.empty")} />;
  }

  // Одна раскладка на шапку и на строки: иначе колонки заголовков разъедутся с ячейками.
  const grid = rowGridFor(showAlbum, playedAt !== undefined);

  const rows = tracks.map((track, index) => (
    <TrackRow
      key={playlistId ? `${track.id}-${index}` : track.id}
      track={track}
      index={index}
      grid={grid}
      sortable={sortable}
      showCover={showCover}
      showAlbum={showAlbum}
      showArtist={showArtist}
      useTrackNumbers={useTrackNumbers}
      playedAt={playedAt}
      playlistId={playlistId}
      isCurrent={player.currentTrack?.id === track.id}
      isPlaying={player.currentTrack?.id === track.id && player.isPlaying}
      isFavorite={isFavorite(track)}
      menuOpen={menuFor === track.id}
      onMenuOpenChange={(open) => setMenuFor(open ? track.id : null)}
      onPlay={() => play(index)}
      onToggleFavorite={() => void toggleFavorite(track)}
      onChanged={changed}
      loadPlaylists={loadPlaylists}
      onQueue={() => {
        player.addToQueue(track);
        notify(t("menu.addedToQueue", { title: track.title }), "success");
      }}
    />
  ));

  const body = (
    <div className="flex flex-col" role="table">
      <div className={cn(grid, "rounded-none border-b border-border pb-2")} role="row">
        <Overline role="columnheader" className="max-md:invisible">
          #
        </Overline>
        <Overline role="columnheader" className="truncate">
          {t("column.title")}
        </Overline>
        {showAlbum && (
          <Overline role="columnheader" className="truncate max-md:hidden">
            {t("column.album")}
          </Overline>
        )}
        {playedAt && (
          <Overline role="columnheader" className="truncate max-md:hidden">
            {t("column.played")}
          </Overline>
        )}
        <span role="columnheader" aria-label={t("column.actions")} />
        <Overline role="columnheader" className="text-right max-[380px]:hidden">
          {t("column.duration")}
        </Overline>
      </div>

      {rows}
    </div>
  );

  if (!sortable) return body;

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={closestCenter}
      modifiers={[restrictToVerticalAxis, restrictToParentElement]}
      onDragEnd={onDragEnd}
    >
      <SortableContext
        items={tracks.map((track) => track.id)}
        strategy={verticalListSortingStrategy}
      >
        {body}
      </SortableContext>
    </DndContext>
  );
}

function TrackRow({
  track,
  index,
  grid,
  sortable,
  showCover,
  showAlbum,
  showArtist,
  useTrackNumbers,
  playedAt,
  playlistId,
  isCurrent,
  isPlaying,
  isFavorite,
  menuOpen,
  onMenuOpenChange,
  onPlay,
  onToggleFavorite,
  onChanged,
  loadPlaylists,
  onQueue,
}: {
  track: Track;
  index: number;
  grid: string;
  sortable: boolean;
  showCover: boolean;
  showAlbum: boolean;
  showArtist: boolean;
  useTrackNumbers: boolean;
  playedAt?: Record<string, string>;
  playlistId?: string;
  isCurrent: boolean;
  isPlaying: boolean;
  isFavorite: boolean;
  menuOpen: boolean;
  onMenuOpenChange: (open: boolean) => void;
  onPlay: () => void;
  onToggleFavorite: () => void;
  onChanged: () => void;
  loadPlaylists: () => Promise<Playlist[]>;
  onQueue: () => void;
}) {
  const t = useT();
  const format = useFormat();

  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: track.id,
    disabled: !sortable,
  });

  return (
    <div
      ref={sortable ? setNodeRef : undefined}
      role="row"
      onDoubleClick={onPlay}
      style={sortable ? { transform: CSS.Transform.toString(transform), transition } : undefined}
      className={cn(
        grid,
        "group relative transition-colors",
        "hover:bg-card focus-within:bg-card",
        isCurrent && "bg-card",
        isDragging && "z-10 opacity-90 shadow-pop",
      )}
    >
      <span className="flex items-center gap-1 text-sm text-faint tabular-nums" role="cell">
        {sortable && (
          <button
            type="button"
            {...attributes}
            {...listeners}
            aria-label={t("tracks.reorderNamed", { title: track.title })}
            className="cursor-grab text-faint opacity-0 transition-opacity group-hover:opacity-100 focus-visible:opacity-100 active:cursor-grabbing max-md:opacity-100"
          >
            <GripIcon size={14} />
          </button>
        )}

        <span className="group-hover:hidden max-md:hidden">
          {useTrackNumbers ? (track.trackNumber ?? index + 1) : index + 1}
        </span>

        <PressButton
          variant="ghost"
          size="icon-sm"
          className="hidden text-foreground group-hover:grid max-md:grid max-md:size-8"
          onClick={onPlay}
          aria-label={
            isPlaying
              ? t("tracks.pauseNamed", { title: track.title })
              : t("tracks.playNamed", { title: track.title })
          }
        >
          {isPlaying ? <PauseIcon size={14} /> : <PlayIcon size={14} />}
        </PressButton>
      </span>

      <span className="flex min-w-0 items-center gap-3" role="cell">
        {showCover && (
          <Cover
            albumId={track.albumId}
            trackId={track.id}
            hasCover={track.hasCover}
            name={track.albumTitle ?? track.title}
            size={40}
          />
        )}
        <span className="flex min-w-0 flex-col">
          <span className={cn("truncate font-semibold", isCurrent && "text-primary")}>
            {track.title}
          </span>
          {showArtist && (
            <ArtistLinks track={track} className="truncate text-sm text-muted-foreground" />
          )}
        </span>
      </span>

      {showAlbum && (
        <span className="truncate text-sm text-muted-foreground max-md:hidden" role="cell">
          {track.albumId ? (
            <Link href={`/albums/${track.albumId}`}>{track.albumTitle}</Link>
          ) : (
            <span>—</span>
          )}
        </span>
      )}

      {playedAt && (
        <span className="truncate text-sm text-muted-foreground max-md:hidden" role="cell">
          {playedAt[track.id] ? format.relativeDate(playedAt[track.id]) : ""}
        </span>
      )}

      <span
        role="cell"
        className={cn(
          "flex items-center justify-end gap-0.5 opacity-0 transition-opacity",
          "group-hover:opacity-100 group-focus-within:opacity-100 max-md:opacity-100",
          isCurrent && "opacity-100",
        )}
      >
        <Button
          variant="ghost"
          size="icon"
          className={cn(isFavorite && "text-primary opacity-100")}
          onClick={onToggleFavorite}
          aria-label={isFavorite ? t("tracks.removeFromFavorites") : t("tracks.addToFavorites")}
          aria-pressed={isFavorite}
        >
          <HeartIcon size={16} filled={isFavorite} />
        </Button>

        <TrackMenu
          track={track}
          open={menuOpen}
          onOpenChange={onMenuOpenChange}
          playlistId={playlistId}
          onChanged={onChanged}
          loadPlaylists={loadPlaylists}
          onQueue={onQueue}
        />
      </span>

      <span
        className="text-right text-sm text-muted-foreground tabular-nums max-[380px]:hidden"
        role="cell"
      >
        {formatDuration(track.durationSeconds)}
      </span>
    </div>
  );
}
