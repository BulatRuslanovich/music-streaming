// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

/**
 * Общие рецепты раскладки коллекций. Раньше эти строки жили в четырёх копиях
 * (CardGrid, Shelf, PosterGrid, QuickRow) и незаметно расходились.
 */

/**
 * Сетка карточек-обложек.
 *
 * Три ступени, а не две. Между 900 и 1280px — ноутбук и планшет в альбомной: нижней панели,
 * как на телефоне, ещё нет, а места уже нет. Карточки в 11rem там оставляли в ряду три штуки
 * вместо пяти, и страница читалась как увеличенный телефон. Tailwind сортирует `max-*` по
 * убыванию, поэтому ниже 900px `max-md` перекрывает `max-xl` — порядок здесь не случайный.
 */
export const cardGrid = [
  "grid grid-cols-[repeat(auto-fill,minmax(11rem,1fr))] gap-6",
  "max-xl:grid-cols-[repeat(auto-fill,minmax(9.5rem,1fr))] max-xl:gap-4",
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
  "max-xl:auto-cols-[9.5rem] max-xl:gap-4",
  "[scroll-snap-type:x_proximity] [&>*]:[scroll-snap-align:start]",
  hiddenScrollbar,
  // На 390px это две полные карточки и ещё 85% третьей: выглядывающий край сам объясняет,
  // что полку надо листать. При прежних 8.75rem их помещалось 2.55 — обрез читался как край.
  "max-md:auto-cols-[7.25rem] max-md:gap-3",
  "max-[380px]:auto-cols-[6.5rem]",
].join(" ");

/** Тот же скрытый скроллбар для лент вне `cardShelf` — например, для ряда плиток. */
export const shelfScrollbar = hiddenScrollbar;

/** Затухание у правого края прокручиваемой ленты (на мобильных отключено). */
export const scrollFade =
  "[mask-image:linear-gradient(to_right,#000_calc(100%-3.5rem),transparent)] max-md:[mask-image:none]";

/**
 * Подрезка блоков на узком экране. Сетка новинок и чарт — единственные блоки, которые на телефоне
 * растут вертикально: полка остаётся высотой в ряд, а они разворачивались на шесть, и вдвоём
 * занимали 43% высоты главной.
 *
 * Прячем через nth-child, а не режем массив в JS, чтобы контекст воспроизведения остался полным:
 * тап по видимой пятой строке чарта по-прежнему ставит в очередь все двенадцать.
 *
 * Обложки скрытых карточек не запрашиваются, потому что Cover жёстко ставит `loading="lazy"`,
 * а ленивая картинка вне вьюпорта не грузится. Уйдёт lazy — трафик вернётся молча.
 *
 * Классы обязаны быть литералами: Tailwind v4 сканирует исходники, и собранное из шаблонной
 * строки имя просто не попадёт в CSS — без ошибки, просто без подрезки.
 */
export const capFourOnMobile = "max-md:[&>*:nth-child(n+5)]:hidden";
export const capFiveOnMobile = "max-md:[&>*:nth-child(n+6)]:hidden";
export const capEightOnMobile = "max-md:[&>*:nth-child(n+9)]:hidden";

/**
 * Секция считается и рисуется, только когда доезжает до экрана. `auto` в contain-intrinsic-size
 * запоминает настоящую высоту после первого показа, поэтому литерал важен лишь до него.
 * Только для зоны Browse: Lead и Quick и так над сгибом, а пропуск-и-возврат даёт у них мигание.
 */
export const deferredSection = "[content-visibility:auto] [contain-intrinsic-size:auto_18rem]";
