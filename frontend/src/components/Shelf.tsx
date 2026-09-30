// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { ChevronLeftIcon, ChevronRightIcon } from "lucide-react";
import type { Route } from "next";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { cn } from "@/lib/cn";
import { useT } from "@/contexts/I18nContext";
import { SectionHeader } from "./PageHeader";
import { Button } from "./ui/button";

/**
 * Полоса прокрутки у лент скрыта намеренно. Прокрутку она не отменяет: на десктопе у полки
 * есть стрелки, на тач-устройствах — свайп, с клавиатуры карточки доводятся табом. А сама
 * полоса тянулась серой чертой под каждой витриной и спорила с обложками.
 */
const hiddenScrollbar = "[scrollbar-width:none] [&::-webkit-scrollbar]:hidden";

/** Горизонтальная лента карточек. */
const cardShelf = [
  "grid grid-flow-col auto-cols-[11rem] gap-6 overflow-x-auto overscroll-x-contain",
  "max-xl:auto-cols-[9.5rem] max-xl:gap-4",
  "[scroll-snap-type:x_proximity] [&>*]:[scroll-snap-align:start]",
  hiddenScrollbar,
  // На 390px это две полные карточки и ещё 85% третьей: выглядывающий край сам объясняет,
  // что полку надо листать. При прежних 8.75rem их помещалось 2.55 — обрез читался как край.
  "max-md:auto-cols-[7.25rem] max-md:gap-3",
  "max-[380px]:auto-cols-[6.5rem]",
].join(" ");

/** Затухание у правого края прокручиваемой ленты (на мобильных отключено). */
const scrollFade =
  "[mask-image:linear-gradient(to_right,#000_calc(100%-3.5rem),transparent)] max-md:[mask-image:none]";

/** Та же подрезка, что у блоков главной (см. `home/layout.ts`): на телефоне полка — восемь карточек. */
const capEightOnMobile = "max-md:[&>*:nth-child(n+9)]:hidden";

/**
 * Горизонтальная лента карточек. Стрелки нужны мыши: колесо ленту вбок не листает. На
 * телефоне их нет — там свайп, а выглядывающая следующая карточка сама говорит «листай».
 */
export function Shelf<T extends string>({
  title,
  note,
  href,
  className,
  children,
}: {
  title: string;
  note?: string;
  href?: Route<T>;
  className?: string;
  children: ReactNode;
}) {
  const t = useT();
  const shelf = useRef<HTMLDivElement>(null);
  const [edges, setEdges] = useState({ atStart: true, atEnd: true });

  useEffect(() => {
    const element = shelf.current;
    if (!element) return;

    const update = () => {
      const furthest = element.scrollWidth - element.clientWidth;
      setEdges({ atStart: element.scrollLeft <= 1, atEnd: element.scrollLeft >= furthest - 1 });
    };

    const resize = new ResizeObserver(update);
    resize.observe(element);
    element.addEventListener("scroll", update, { passive: true });

    return () => {
      resize.disconnect();
      element.removeEventListener("scroll", update);
    };
  }, []);

  const scroll = (direction: 1 | -1) => {
    const element = shelf.current;
    element?.scrollBy({ left: direction * element.clientWidth * 0.8, behavior: "smooth" });
  };

  return (
    <section className={cn("group/section flex flex-col gap-4", className)}>
      <SectionHeader
        title={title}
        note={note}
        href={href}
        actions={
          // Полка, которой некуда листать (один альбом в дискографии), стрелок не показывает:
          // две выключенные стрелки выглядели как сломанная навигация.
          !(edges.atStart && edges.atEnd) && (
            <div className="flex gap-1 opacity-0 transition-opacity duration-150 ease-brand group-focus-within/section:opacity-100 group-hover/section:opacity-100 max-md:hidden">
              <Button
                variant="ghost"
                size="icon"
                onClick={() => scroll(-1)}
                disabled={edges.atStart}
                aria-label={t("shelf.scrollBackwards", { title })}
              >
                <ChevronLeftIcon />
              </Button>
              <Button
                variant="ghost"
                size="icon"
                onClick={() => scroll(1)}
                disabled={edges.atEnd}
                aria-label={t("shelf.scrollForwards", { title })}
              >
                <ChevronRightIcon />
              </Button>
            </div>
          )
        }
      />

      <div ref={shelf} className={cn(cardShelf, scrollFade, capEightOnMobile, "pb-2")}>
        {children}
      </div>
    </section>
  );
}
