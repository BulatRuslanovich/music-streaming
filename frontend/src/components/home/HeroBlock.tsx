"use client";

import { cn } from "@/lib/cn";
import { formatArtists, formatDuration } from "@/lib/format";
import { trackCoverUrl } from "@/lib/media";
import { useCoverColor } from "@/lib/useCoverColor";
import { useFormat } from "@/lib/useFormat";
import type { HomeBlock } from "@/lib/types";
import { usePlayer, type PlaybackOrigin } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { Cover } from "../Cover";
import { PlayAllButton } from "../PlayAllButton";
import { Overline } from "../ui/label";
import { CoverMosaic } from "./CoverMosaic";

const PREVIEW_SIZE = 4;

export function HeroBlock({
  block,
  title,
  origin,
}: {
  block: HomeBlock;
  title: string;
  origin: PlaybackOrigin;
}) {
  const t = useT();
  const format = useFormat();
  const player = usePlayer();

  const tracks = block.tracks ?? [];
  const tint = useCoverColor(trackCoverUrl(tracks[0], "thumb"));

  const duration = tracks.reduce((total, track) => total + track.durationSeconds, 0);

  return (
    <section
      style={{ ["--art-tint" as string]: tint ?? "" }}
      className={cn(
        "flex gap-8 rounded-xl border border-border p-5 max-lg:flex-col max-md:gap-4 max-md:p-3",
        "bg-[linear-gradient(140deg,color-mix(in_srgb,var(--art-tint)_38%,transparent),transparent_70%)]",
        "[transition:--art-tint_700ms_var(--ease)]",
      )}
    >
      <div className="flex min-w-0 flex-1 items-end gap-5 max-md:items-start max-md:gap-3">
        <div className="size-44 shrink-0 overflow-hidden rounded-lg shadow-art max-md:size-28">
          <CoverMosaic tracks={tracks} />
        </div>

        <div className="flex min-w-0 flex-col gap-2">
          <Overline>{t("home.dailyMixSubtitle")}</Overline>
          <h2 className="text-[clamp(1.5rem,1.1rem+1.4vw,2.25rem)]">{title}</h2>
          <p className="text-sm text-muted-foreground">
            {t("count.tracks", { count: tracks.length })} · {format.totalDuration(duration)}
          </p>
          <div className="mt-1">
            <PlayAllButton tracks={tracks} name={title} />
          </div>
        </div>
      </div>

      <ol className="flex w-88 shrink-0 flex-col gap-0.5 max-lg:w-full">
        {tracks.slice(0, PREVIEW_SIZE).map((track, index) => {
          const isCurrent = player.currentTrack?.id === track.id;

          return (
            <li key={track.id}>
              <button
                type="button"
                onClick={() => {
                  if (isCurrent) {
                    player.toggle();
                    return;
                  }
                  player.playTrack(track, tracks, origin);
                }}
                className={cn(
                  "grid w-full grid-cols-[1.25rem_2.25rem_minmax(0,1fr)_auto] items-center gap-3 rounded-md px-2 py-1.5 text-left",
                  "transition-colors duration-150 ease-brand hover:bg-raised",
                )}
              >
                <span className="text-sm text-faint tabular-nums">{index + 1}</span>

                <Cover
                  albumId={track.albumId}
                  trackId={track.id}
                  hasCover={track.hasCover}
                  name={track.albumTitle ?? track.title}
                  className="size-9"
                />

                <span className="min-w-0">
                  <span className={cn("block truncate", isCurrent && "text-primary")}>
                    {track.title}
                  </span>
                  <span className="block truncate text-sm text-muted-foreground">
                    {formatArtists(track)}
                  </span>
                </span>

                <span className="text-sm text-faint tabular-nums">
                  {formatDuration(track.durationSeconds)}
                </span>
              </button>
            </li>
          );
        })}
      </ol>
    </section>
  );
}
