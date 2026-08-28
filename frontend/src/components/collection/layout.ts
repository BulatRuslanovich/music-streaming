// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

/**
 * Общие рецепты раскладки коллекций. Раньше эти строки жили в четырёх копиях
 * (CardGrid, Shelf, PosterGrid, QuickRow) и незаметно расходились.
 */

/** Сетка карточек-обложек. */
export const cardGrid = [
  "grid grid-cols-[repeat(auto-fill,minmax(11rem,1fr))] gap-6",
  "max-md:grid-cols-[repeat(auto-fill,minmax(8.75rem,1fr))] max-md:gap-3",
  "max-[380px]:grid-cols-[repeat(auto-fill,minmax(7.6rem,1fr))]",
].join(" ");

/**
 * Полоса прокрутки у лент скрыта намеренно. Прокрутку она не отменяет: на десктопе у полки
 * есть стрелки, на тач-устройствах — свайп, с клавиатуры карточки доводятся табом. А сама
 * полоса тянулась серой чертой под каждой витриной и спорила с обложками.
 */
const hiddenScrollbar = "[scrollbar-width:none] [&::-webkit-scrollbar]:hidden";

/** Горизонтальная лента карточек. */
export const cardShelf = [
  "grid grid-flow-col auto-cols-[11rem] gap-6 overflow-x-auto overscroll-x-contain",
  "[scroll-snap-type:x_proximity] [&>*]:[scroll-snap-align:start]",
  hiddenScrollbar,
  "max-md:auto-cols-[8.75rem] max-md:gap-3",
].join(" ");

/** Тот же скрытый скроллбар для лент вне `cardShelf` — например, для ряда плиток. */
export const shelfScrollbar = hiddenScrollbar;

/** Затухание у правого края прокручиваемой ленты (на мобильных отключено). */
export const scrollFade =
  "[mask-image:linear-gradient(to_right,#000_calc(100%-3.5rem),transparent)] max-md:[mask-image:none]";
