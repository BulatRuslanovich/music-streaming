"use client";

import { useEffect } from "react";

export function EasterEgg({ open, onClose }: { open: boolean; onClose: () => void }) {
  useEffect(() => {
    if (!open) return;

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div
      className="modal-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <div className="modal easter-egg" role="dialog" aria-modal="true" aria-label="Секрет">
        <div className="boot-equalizer easter-egg-equalizer" aria-hidden="true">
          <span />
          <span />
          <span />
          <span />
        </div>

        <p className="easter-egg-text">
          Вообще, название проекта придумал один из моих младших братьев, но изначально
          предназначался для ника самого младшего. 
        </p>

        <p className="easter-egg-text">
          <strong>Caimack</strong> — не означает не большее, не меньшее.
           Надеюсь, самый младший придумает ник получше, чем
          производная от молока.
        </p>

        <p className="easter-egg-signature">— getname</p>

        <button type="button" className="button easter-egg-close" onClick={onClose}>
          Закрыть
        </button>
      </div>
    </div>
  );
}
