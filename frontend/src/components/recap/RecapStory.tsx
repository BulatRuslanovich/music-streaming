// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import * as DialogPrimitive from "@radix-ui/react-dialog";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import { useMemo } from "react";
import { CoverBackdrop } from "@/components/AmbientBackdrop";
import { CloseIcon } from "@/components/Icons";
import { RecapSlide } from "@/components/recap/RecapSlide";
import { Button } from "@/components/ui/button";
import { useT } from "@/contexts/I18nContext";
import { cn } from "@/lib/cn";
import { mediaUrl, trackCoverUrl } from "@/lib/media";
import { DURATION, EASE } from "@/lib/motion";
import type { MonthlyRecap } from "@/lib/recap";
import { recapSlides, type RecapSlide as Slide } from "@/lib/recapStory";
import { SLIDE_MS, useStoryPlayback } from "@/lib/useStoryPlayback";

/**
 * Итоги месяца как история.
 *
 * Оверлей на Radix, а не самодельный: у `FullScreenPlayer` в комментарии записано, чем
 * кончается ручная модалка — фокус не переносится внутрь, табом уходишь на страницу под
 * ней, фон не скрыт от скринридера. Escape и возврат фокуса тоже достаются отсюда.
 *
 * Фон каждого слайда — размытая настоящая обложка этого слайда плюс зерно. Цвет не
 * вычисляется: экран читается как «этот альбом», а не как абстрактное пятно, и это
 * единственное место в приложении, где движение уместно — история никуда не прокручивается,
 * она сама себе носитель.
 */
export function RecapStory({ data, onClose }: { data: MonthlyRecap; onClose: () => void }) {
  const t = useT();
  const reduceMotion = useReducedMotion();

  const slides = useMemo(() => recapSlides(data), [data]);
  const story = useStoryPlayback({
    count: slides.length,
    // Автопрокрутка — это движение без спроса. Кому оно противопоказано, тот листает сам.
    autoplay: !reduceMotion,
    onFinish: onClose,
  });

  const slide = slides[story.index];
  if (!slide) return null;

  return (
    <DialogPrimitive.Root open onOpenChange={(next) => !next && onClose()}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Content asChild aria-describedby={undefined}>
          <motion.div
            initial={reduceMotion ? false : { opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: DURATION * 2, ease: EASE }}
            className={cn(
              "fixed inset-0 z-90 flex flex-col bg-background select-none",
              "pt-[max(0.75rem,env(safe-area-inset-top))] pb-[max(1.5rem,env(safe-area-inset-bottom))]",
            )}
            onKeyDown={(event) => {
              if (event.key === "ArrowRight") story.next();
              if (event.key === "ArrowLeft") story.previous();
            }}
          >
            <DialogPrimitive.Title className="sr-only">{t("recap.title")}</DialogPrimitive.Title>

            <CoverBackdrop source={backdropFor(slide, data)} />

            {/*
              Зоны листания идут первыми: у позиционированных элементов без z-index порядок
              наложения — это порядок в разметке, и стой они ниже, «следующий слайд» накрыл бы
              собой крестик и кнопку подробностей. Удержание ставит паузу — как в сторис.
            */}
            <div
              className="absolute inset-0 flex"
              onPointerDown={() => story.hold(true)}
              onPointerUp={() => story.hold(false)}
              onPointerCancel={() => story.hold(false)}
              onPointerLeave={() => story.hold(false)}
            >
              <button
                type="button"
                aria-label={t("recap.storyPrevious")}
                className="h-full w-1/3 cursor-w-resize"
                onClick={story.previous}
              />
              <button
                type="button"
                aria-label={t("recap.storyNext")}
                className="h-full flex-1 cursor-e-resize"
                onClick={story.next}
              />
            </div>

            <Progress count={slides.length} index={story.index} paused={story.paused} />

            <div className="relative flex items-center justify-between px-5 pt-3">
              <span className="text-2xs font-bold tracking-[0.08em] text-faint uppercase">
                {t("recap.storyStep", { current: story.index + 1, total: slides.length })}
              </span>

              <DialogPrimitive.Close asChild>
                <Button variant="ghost" size="icon" aria-label={t("recap.storyClose")}>
                  <CloseIcon size={18} />
                </Button>
              </DialogPrimitive.Close>
            </div>

            <div className="pointer-events-none relative flex flex-1 items-center px-10 max-md:px-6">
              <AnimatePresence mode="wait">
                <motion.div
                  key={story.index}
                  initial={reduceMotion ? false : { opacity: 0 }}
                  animate={{ opacity: 1 }}
                  exit={{ opacity: 0 }}
                  transition={{ duration: DURATION, ease: EASE }}
                  className="w-full"
                  aria-live="polite"
                >
                  <RecapSlide slide={slide} data={data} />
                </motion.div>
              </AnimatePresence>
            </div>

            <div className="relative flex justify-center px-6">
              <Button variant="secondary" onClick={onClose}>
                {t("recap.storyDetails")}
              </Button>
            </div>
          </motion.div>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}

function Progress({ count, index, paused }: { count: number; index: number; paused: boolean }) {
  const t = useT();

  return (
    <div className="relative flex gap-1 px-5" role="group" aria-label={t("recap.storyProgress")}>
      {Array.from({ length: count }, (_, position) => (
        <span key={position} className="h-0.5 flex-1 overflow-hidden rounded-full bg-white/25">
          <span
            // Ключ по индексу перезапускает анимацию на каждом слайде; длительность та же, что
            // у таймера, а пауза у них общая — поэтому полоска не расходится со сменой экрана.
            key={`${position}:${index}`}
            className={cn(
              "block h-full origin-left bg-white",
              position < index && "scale-x-100",
              position > index && "scale-x-0",
              position === index && "animate-story",
            )}
            style={
              position === index
                ? {
                    animationDuration: `${SLIDE_MS}ms`,
                    animationPlayState: paused ? "paused" : "running",
                  }
                : undefined
            }
          />
        </span>
      ))}
    </div>
  );
}

/** Обложка, которой красится экран: у каждого слайда своя, иначе фон перестаёт что-то значить. */
function backdropFor(slide: Slide, data: MonthlyRecap): string | null {
  switch (slide.kind) {
    case "topTrack":
      return trackCoverUrl(slide.entry.track, "thumb");
    case "topArtist":
      return slide.entry.hasImage ? mediaUrl.artistImage(slide.entry.id, "thumb") : null;
    case "discoveries": {
      const withImage = slide.entries.find((entry) => entry.hasImage);
      return withImage ? mediaUrl.artistImage(withImage.id, "thumb") : null;
    }
    default:
      return trackCoverUrl(data.topTracks[0]?.track, "thumb");
  }
}
