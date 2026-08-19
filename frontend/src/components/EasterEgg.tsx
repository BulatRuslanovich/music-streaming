// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { Dialog, DialogContent } from "./ui/dialog";

export function EasterEgg({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent title="Секретка" className="border-primary/45">
        <div className="flex flex-col items-center gap-5 text-center">
          <div className="flex h-8 items-end gap-1.5" aria-hidden="true">
            {[0, 1, 2, 3].map((bar) => (
              <span
                key={bar}
                className="w-1.5 animate-equalize rounded-full bg-primary"
                style={{ animationDelay: `${-0.9 + bar * 0.25}s` }}
              />
            ))}
          </div>

          <p className="leading-relaxed">
            Вообще, название проекта придумал один из моих младших братьев, но изначально оно
            предназначалось для ника самого младшего.
          </p>

          <p className="leading-relaxed">
            <strong>Caimack</strong> — не больше и не меньше. Никакого глубокого смысла за названием
            нет. Надеюсь, он придумает себе ник получше, чем производная от молока.
          </p>

          <p className="text-lg text-primary italic">— Bulat Ruslanovich</p>
        </div>
      </DialogContent>
    </Dialog>
  );
}
