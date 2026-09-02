// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ReactNode } from "react";
import { Dialog, DialogContent } from "./ui/dialog";

export type EasterEggPage = 1 | 2;

/**
 * Секретка остаётся мимо i18n и по-русски: это записка от автора, а не интерфейс. Страницы
 * открываются разными путями — первая конами-кодом, вторая семью кликами по знаку, — но живут
 * в одном диалоге: вторая находка должна выглядеть продолжением первой, а не другим окном.
 */
export function EasterEgg({ page, onClose }: { page: EasterEggPage | null; onClose: () => void }) {
  return (
    <Dialog open={page !== null} onOpenChange={(next) => !next && onClose()}>
      <DialogContent title="Секретка" className="border-primary/45">
        <div className="flex flex-col items-center gap-5 text-center">
          <Equalizer />
          {page === 2 ? <SecondPage /> : <FirstPage />}
          <p className="text-lg text-primary italic">— Bulat Ruslanovich</p>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function Equalizer(): ReactNode {
  return (
    <div className="flex h-8 items-end gap-1.5" aria-hidden="true">
      {[0, 1, 2, 3].map((bar) => (
        <span
          key={bar}
          className="w-1.5 animate-equalize rounded-full bg-primary"
          style={{ animationDelay: `${-0.9 + bar * 0.25}s` }}
        />
      ))}
    </div>
  );
}

function FirstPage(): ReactNode {
  return (
    <>
      <p className="leading-relaxed">
        Вообще, название проекта придумал один из моих младших братьев, но изначально оно
        предназначалось для ника самого младшего.
      </p>

      <p className="leading-relaxed">
        <strong>Caimack</strong> — не больше и не меньше. Никакого глубокого смысла за названием
        нет. Надеюсь, он придумает себе ник получше, чем производная от молока.
      </p>
    </>
  );
}

function SecondPage(): ReactNode {
  return (
    <>
      <p className="leading-relaxed">Дописываю я уже этот проект в сентебре 2026</p>

      <p className="leading-relaxed">
        У меня начинается магистратура, хз че еще можно тут написать. Ну красавчик, что потыкался ии
        нашел одну из секреток.
      </p>
    </>
  );
}
