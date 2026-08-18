"use client";

import Link from "next/link";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { cn } from "@/lib/cn";
import { useT } from "@/contexts/I18nContext";
import { Button } from "./ui/button";
import { ChevronLeftIcon, ChevronRightIcon } from "./Icons";

export function PageHeader({
  title,
  subtitle,
  actions,
}: {
  title: string;
  subtitle?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <header className="flex flex-wrap items-end justify-between gap-5 max-md:items-start">
      <div className="min-w-0">
        <h1 className="text-[clamp(1.75rem,1.2rem+1.8vw,2.6rem)]">{title}</h1>
        {subtitle && <p className="mt-1 text-sm text-faint">{subtitle}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-4">{actions}</div>}
    </header>
  );
}

export function SectionHeader({
  title,
  href,
  children,
}: {
  title: string;
  href?: string;
  children?: ReactNode;
}) {
  const t = useT();

  return (
    <div className="flex items-center justify-between gap-3">
      <h2 className="text-xl">{title}</h2>
      <div className="flex items-center gap-3">
        {href && (
          <Button variant="text" size="auto" asChild>
            <Link href={href}>{t("action.seeAll")}</Link>
          </Button>
        )}
        {children}
      </div>
    </div>
  );
}

export function CardGrid({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div
      className={cn(
        "grid grid-cols-[repeat(auto-fill,minmax(10.25rem,1fr))] gap-5",
        "max-md:grid-cols-[repeat(auto-fill,minmax(8.75rem,1fr))] max-md:gap-3",
        "max-[380px]:grid-cols-[repeat(auto-fill,minmax(7.6rem,1fr))]",
        "[&>*]:animate-rise",
        className,
      )}
    >
      {children}
    </div>
  );
}

export function Shelf({
  title,
  href,
  children,
}: {
  title: string;
  href?: string;
  children: ReactNode;
}) {
  const t = useT();
  const shelf = useRef<HTMLDivElement>(null);
  const [atStart, setAtStart] = useState(true);
  const [atEnd, setAtEnd] = useState(true);

  useEffect(() => {
    const element = shelf.current;
    if (!element) return;

    const update = () => {
      const furthest = element.scrollWidth - element.clientWidth;
      setAtStart(element.scrollLeft <= 1);
      setAtEnd(element.scrollLeft >= furthest - 1);
    };

    element.addEventListener("scroll", update, { passive: true });

    const observer = new ResizeObserver(update);
    observer.observe(element);

    return () => {
      element.removeEventListener("scroll", update);
      observer.disconnect();
    };
  }, []);

  const scrollShelf = (direction: 1 | -1) => {
    const element = shelf.current;
    if (!element) return;
    element.scrollBy({ left: direction * element.clientWidth * 0.8, behavior: "smooth" });
  };

  return (
    <section className="flex flex-col gap-3">
      <SectionHeader title={title} href={href}>
        <div className="max-md:hidden flex gap-1">
          <Button
            variant="ghost"
            size="icon"
            onClick={() => scrollShelf(-1)}
            disabled={atStart}
            aria-label={t("shelf.scrollBackwards", { title })}
          >
            <ChevronLeftIcon size={20} />
          </Button>
          <Button
            variant="ghost"
            size="icon"
            onClick={() => scrollShelf(1)}
            disabled={atEnd}
            aria-label={t("shelf.scrollForwards", { title })}
          >
            <ChevronRightIcon size={20} />
          </Button>
        </div>
      </SectionHeader>

      <div
        ref={shelf}
        className={cn(
          "grid grid-flow-col auto-cols-[10.25rem] gap-5 overflow-x-auto overscroll-x-contain px-0 pt-1 pb-2 [scroll-snap-type:x_proximity]",
          "[mask-image:linear-gradient(to_right,#000_calc(100%-3.5rem),transparent)]",
          "max-md:auto-cols-[8.75rem] max-md:gap-3 max-md:[mask-image:none]",
          "[&>*]:animate-rise [&>*]:[scroll-snap-align:start]",
        )}
      >
        {children}
      </div>
    </section>
  );
}
