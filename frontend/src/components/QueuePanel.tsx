"use client";

import { formatArtists, formatDuration } from "@/lib/format";
import { usePlayer } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { Cover } from "./Cover";
import { CloseIcon, TrashIcon } from "./Icons";

export function QueuePanel({ onClose }: { onClose: () => void }) {
  const t = useT();

  return (
    <aside className="queue-panel" aria-label={t("queue.label")}>
      <header>
        <h3>{t("queue.title")}</h3>
        <button type="button" className="icon-button" onClick={onClose} aria-label={t("queue.close")}>
          <CloseIcon size={18} />
        </button>
      </header>
      <QueueList />
    </aside>
  );
}

export function QueueList() {
  const player = usePlayer();
  const t = useT();

  if (player.queue.length === 0) {
    return <p className="empty-state">{t("queue.empty")}</p>;
  }

  return (
    <>
      <div className="queue-actions">
        <span className="muted">{t("count.tracks", { count: player.queue.length })}</span>
        <button type="button" className="text-button" onClick={player.clearQueue}>
          {t("action.clear")}
        </button>
      </div>

      <ol className="queue-list">
        {player.queue.map((track, index) => (
          <li
            key={`${track.id}-${index}`}
            className={index === player.currentIndex ? "is-current" : ""}
          >
            <button
              type="button"
              className="queue-item"
              onClick={() => player.jumpTo(index)}
              aria-current={index === player.currentIndex}
            >
              <Cover
                albumId={track.albumId}
                trackId={track.id}
                hasCover={track.hasCover}
                name={track.albumTitle ?? track.title}
                size={36}
              />
              <span className="queue-meta">
                <span className="queue-title">{track.title}</span>
                {/* Plain text, not links: the whole queue row is a button. */}
                <span className="queue-artist">{formatArtists(track)}</span>
              </span>
              <span className="time">{formatDuration(track.durationSeconds)}</span>
            </button>

            <button
              type="button"
              className="icon-button"
              onClick={() => player.removeFromQueue(index)}
              aria-label={t("queue.removeNamed", { title: track.title })}
            >
              <TrashIcon size={15} />
            </button>
          </li>
        ))}
      </ol>
    </>
  );
}
