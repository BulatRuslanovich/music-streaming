// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { genreName } from "@/lib/format";
import {
  useCallback,
  useEffect,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
  type Ref,
} from "react";
import {
  boundsOf,
  centreOn,
  constellation,
  fitView,
  playsLevel,
  pointInPolygon,
  toMap,
  toScreen,
  zoomAt,
  type MapView,
  type Spot,
} from "@/lib/libraryMap";
import { rgbOf, rgba, type Rgb } from "@/lib/cssColor";
import type { MapPoint } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { Cover } from "@/components/Cover";
import { Button } from "@/components/ui/button";
import { PlayIcon, RadioIcon, XIcon } from "lucide-react";

export interface MapLabel {
  text: string;
  x: number;
  y: number;
  strong?: boolean;
  /** Оттенок OKLCH настроения: подпись получает цветную метку и «территорию» вокруг. */
  hue?: number;
}

export interface MapController {
  zoomBy: (factor: number) => void;
  reset: () => void;
  locate: (id: string) => void;
}

const HIT_RADIUS_PX = 14;
const CLICK_SLOP_PX = 5;
const LOCATE_ZOOM = 5;
const LEVELS = 8;
const CARD_WIDTH_PX = 290;
// Радиус «территории» настроения — доля карты.
const TERRITORY = 0.14;

interface Placed {
  point: MapPoint;
  at: Spot;
  flip: boolean;
}

interface Palette {
  primary: Rgb;
  onPrimary: Rgb;
  faint: Rgb;
  ink: Rgb;
  card: Rgb;
  light: boolean;
  font: string;
}

function readPalette(element: Element): Palette {
  const style = getComputedStyle(element);
  const read = (name: string) => style.getPropertyValue(name);

  return {
    primary: rgbOf(read("--primary")),
    onPrimary: rgbOf(read("--primary-foreground")),
    faint: rgbOf(read("--faint-foreground")),
    ink: rgbOf(read("--foreground")),
    card: rgbOf(read("--card"), [0, 0, 0]),
    light: style.colorScheme === "light",
    font: style.fontFamily,
  };
}

// Цвет настроения на карте — тот же приглушённый оттенок, что у плиток «Под настроение».
function moodTint(hue: number, light: boolean, alpha: number): string {
  return `oklch(${light ? 0.58 : 0.72} 0.11 ${hue} / ${alpha})`;
}

// Ореол прослушанного трека рисуется готовой картинкой: градиент на каждую точку — дорого.
function haloSprite(color: Rgb, strength: number): HTMLCanvasElement {
  const size = 64;
  const sprite = document.createElement("canvas");
  sprite.width = size;
  sprite.height = size;

  const context = sprite.getContext("2d")!;
  const glow = context.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
  glow.addColorStop(0, rgba(color, strength));
  glow.addColorStop(0.4, rgba(color, strength * 0.35));
  glow.addColorStop(1, rgba(color, 0));
  context.fillStyle = glow;
  context.fillRect(0, 0, size, size);

  return sprite;
}

function dotRadius(k: number): number {
  return Math.min(6, 2.2 + 0.9 * Math.log2(k + 1));
}

export function LibraryMapCanvas({
  points,
  labels,
  highlightGenre,
  nowPlayingId,
  selecting,
  selectedIds,
  onSelect,
  onPlay,
  onRadio,
  controllerRef,
}: {
  points: MapPoint[];
  labels: MapLabel[];
  highlightGenre: string | null;
  nowPlayingId: string | null;
  selecting: boolean;
  selectedIds: ReadonlySet<string> | null;
  onSelect: (points: MapPoint[]) => void;
  onPlay: (point: MapPoint) => void;
  onRadio: (point: MapPoint) => void;
  controllerRef?: Ref<MapController>;
}) {
  const t = useT();
  const wrapRef = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);

  const viewRef = useRef<MapView | null>(null);
  const sizeRef = useRef({ width: 0, height: 0 });
  const frameRef = useRef<number | null>(null);
  const lassoRef = useRef<Spot[] | null>(null);
  const pointersRef = useRef(new Map<number, Spot>());
  const gestureRef = useRef<{ start: Spot; moved: boolean; pinch: number | null } | null>(null);

  const [hovered, setHovered] = useState<Placed | null>(null);
  const [picked, setPicked] = useState<Placed | null>(null);

  // Карточка у правого края открывается влево от точки, а не уезжает за холст.
  const place = (point: MapPoint, at: Spot): Placed => ({
    point,
    at,
    flip: at.x > sizeRef.current.width - CARD_WIDTH_PX,
  });

  const maxPlays = points.reduce((max, point) => Math.max(max, point.plays), 0);
  const bounds = useMemo(() => boundsOf(points), [points]);
  const links = useMemo(() => constellation(points), [points]);

  const latest = useRef({
    points,
    labels,
    highlightGenre,
    nowPlayingId,
    selectedIds,
    maxPlays,
    bounds,
    links,
  });
  useEffect(() => {
    latest.current = {
      points,
      labels,
      highlightGenre,
      nowPlayingId,
      selectedIds,
      maxPlays,
      bounds,
      links,
    };
  });

  const halo = useRef<{ key: string; sprite: HTMLCanvasElement } | null>(null);

  const hoveredId = hovered?.point.id ?? picked?.point.id ?? null;
  const hoveredRef = useRef<string | null>(null);
  useEffect(() => {
    hoveredRef.current = hoveredId;
  });

  const draw = useCallback(() => {
    frameRef.current = null;

    const canvas = canvasRef.current;
    const view = viewRef.current;
    const { width, height } = sizeRef.current;
    const context = canvas?.getContext("2d");
    if (!canvas || !context || !view || width === 0) return;

    const { points, labels, highlightGenre, nowPlayingId, selectedIds, maxPlays, links } =
      latest.current;
    const palette = readPalette(canvas);
    const ratio = window.devicePixelRatio || 1;
    const base = Math.min(width, height);
    const radius = dotRadius(view.k);

    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    context.clearRect(0, 0, width, height);

    // Сетка-градусник: точки с равным шагом карты, шаг мельчает при приближении — видно, что карта движется.
    const step = 0.1 / 2 ** Math.max(0, Math.floor(Math.log2(view.k)));
    const from = toMap({ x: 0, y: 0 }, view, base);
    const to = toMap({ x: width, y: height }, view, base);
    context.fillStyle = rgba(palette.faint, palette.light ? 0.3 : 0.2);
    context.beginPath();
    for (let gx = Math.floor(from.x / step) * step; gx <= to.x; gx += step) {
      for (let gy = Math.floor(from.y / step) * step; gy <= to.y; gy += step) {
        const spot = toScreen({ x: gx, y: gy }, view, base);
        context.moveTo(spot.x + 1, spot.y);
        context.arc(spot.x, spot.y, 1, 0, Math.PI * 2);
      }
    }
    context.fill();

    // Территории настроений — мягкие пятна их оттенка под подписями.
    for (const label of labels) {
      if (label.hue === undefined) continue;

      const spot = toScreen(label, view, base);
      const reach = TERRITORY * base * view.k;
      const glow = context.createRadialGradient(spot.x, spot.y, 0, spot.x, spot.y, reach);
      glow.addColorStop(0, moodTint(label.hue, palette.light, palette.light ? 0.2 : 0.16));
      glow.addColorStop(1, moodTint(label.hue, palette.light, 0));
      context.fillStyle = glow;
      context.fillRect(spot.x - reach, spot.y - reach, reach * 2, reach * 2);
    }

    // Созвездия: похожие по звучанию соседи связаны тонкой линией.
    context.beginPath();
    for (const [a, b] of links) {
      const start = toScreen(points[a], view, base);
      const end = toScreen(points[b], view, base);
      context.moveTo(start.x, start.y);
      context.lineTo(end.x, end.y);
    }
    context.lineWidth = 1;
    context.strokeStyle = rgba(palette.ink, palette.light ? 0.13 : 0.09);
    context.stroke();

    // true/false — точка в фокусе (выделение или жанр) или в фоне; null — фокуса нет.
    const emphasised = (point: MapPoint) =>
      selectedIds
        ? selectedIds.has(point.id)
        : highlightGenre
          ? point.genre === highlightGenre
          : null;

    // Точки раскладываются по корзинам цвета, чтобы не менять fillStyle на каждой.
    const buckets = new Map<string, Spot[]>();
    const glowing: { spot: Spot; level: number }[] = [];
    const front: Spot[] = [];
    let current: Spot | null = null;
    let ring: Spot | null = null;

    for (const point of points) {
      const spot = toScreen(point, view, base);
      if (
        spot.x < -radius ||
        spot.y < -radius ||
        spot.x > width + radius ||
        spot.y > height + radius
      )
        continue;

      if (point.id === nowPlayingId) current = spot;
      if (point.id === hoveredRef.current) ring = spot;

      const marked = emphasised(point);
      if (marked === true) {
        front.push(spot);
        continue;
      }

      let fill: string;
      if (marked === false) {
        fill = rgba(palette.faint, 0.14);
      } else {
        const level = Math.round(playsLevel(point.plays, maxPlays) * LEVELS) / LEVELS;
        fill = level === 0 ? rgba(palette.faint, 0.42) : rgba(palette.primary, 0.3 + 0.7 * level);
        if (level > 0) glowing.push({ spot, level });
      }

      const bucket = buckets.get(fill);
      if (bucket) bucket.push(spot);
      else buckets.set(fill, [spot]);
    }

    const dots = (spots: Spot[], size: number) => {
      context.beginPath();
      for (const spot of spots) {
        context.moveTo(spot.x + size, spot.y);
        context.arc(spot.x, spot.y, size, 0, Math.PI * 2);
      }
      context.fill();
    };

    // Чем чаще слушали трек, тем шире его янтарный ореол.
    const haloKey = `${palette.primary.join()}:${palette.light}`;
    if (halo.current?.key !== haloKey) {
      halo.current = {
        key: haloKey,
        sprite: haloSprite(palette.primary, palette.light ? 0.3 : 0.4),
      };
    }
    const sprite = halo.current.sprite;

    context.globalCompositeOperation = palette.light ? "multiply" : "lighter";
    for (const { spot, level } of glowing) {
      const reach = radius * (2 + 3.5 * level);
      context.drawImage(sprite, spot.x - reach, spot.y - reach, reach * 2, reach * 2);
    }
    context.globalCompositeOperation = "source-over";

    for (const [fill, spots] of buckets) {
      context.fillStyle = fill;
      dots(spots, radius);
    }

    if (front.length > 0) {
      context.fillStyle = rgba(palette.primary, 1);
      dots(front, radius + 0.5);
    }

    context.lineWidth = 2;
    context.strokeStyle = rgba(palette.ink, 1);

    if (current) {
      const reach = radius * 9;
      context.drawImage(sprite, current.x - reach, current.y - reach, reach * 2, reach * 2);
      context.fillStyle = rgba(palette.primary, 1);
      dots([current], radius + 1.5);
      context.beginPath();
      context.arc(current.x, current.y, radius + 5, 0, Math.PI * 2);
      context.stroke();
    }

    if (ring) {
      context.beginPath();
      context.arc(ring.x, ring.y, radius + 3, 0, Math.PI * 2);
      context.stroke();
    }

    context.textAlign = "left";
    context.textBaseline = "middle";

    // Подписи — таблички; упавшие друг на друга расходятся по вертикали.
    const placed: { left: number; right: number; top: number; bottom: number }[] = [];

    for (const label of [...labels].sort(
      (a, b) => Number(b.strong ?? false) - Number(a.strong ?? false),
    )) {
      const spot = toScreen(label, view, base);
      const size = label.strong ? 14 : 12;
      context.font = `${label.strong ? 700 : 600} ${size}px ${palette.font}`;

      const mark = label.hue === undefined ? 0 : 13;
      const plate = context.measureText(label.text).width + 20 + mark;
      const height = size + 12;
      let y = spot.y;

      for (let attempt = 0; attempt < 6; attempt++) {
        const box = {
          left: spot.x - plate / 2 - 4,
          right: spot.x + plate / 2 + 4,
          top: y - height / 2 - 2,
          bottom: y + height / 2 + 2,
        };
        const hit = placed.find(
          (other) =>
            box.left < other.right &&
            box.right > other.left &&
            box.top < other.bottom &&
            box.bottom > other.top,
        );
        if (!hit) {
          placed.push(box);
          break;
        }
        y = hit.bottom + height / 2 + 2;
      }

      const left = spot.x - plate / 2;
      context.beginPath();
      context.roundRect(left, y - height / 2, plate, height, height / 2);
      context.fillStyle = label.strong ? rgba(palette.primary, 1) : rgba(palette.card, 0.86);
      context.fill();

      if (!label.strong) {
        context.lineWidth = 1;
        context.strokeStyle =
          label.hue === undefined
            ? rgba(palette.ink, 0.16)
            : moodTint(label.hue, palette.light, 0.55);
        context.stroke();
      }

      if (label.hue !== undefined) {
        context.beginPath();
        context.arc(left + 13, y, 3.5, 0, Math.PI * 2);
        context.fillStyle = moodTint(label.hue, palette.light, 1);
        context.fill();
      }

      context.fillStyle = label.strong ? rgba(palette.onPrimary, 1) : rgba(palette.ink, 1);
      context.fillText(label.text, left + 10 + mark, y + 0.5);
    }

    const lasso = lassoRef.current;
    if (lasso && lasso.length > 1) {
      context.beginPath();
      context.moveTo(lasso[0].x, lasso[0].y);
      for (const spot of lasso) context.lineTo(spot.x, spot.y);
      context.closePath();
      context.fillStyle = rgba(palette.primary, 0.08);
      context.fill();
      context.setLineDash([6, 4]);
      context.lineWidth = 1.5;
      context.strokeStyle = rgba(palette.ink, 1);
      context.stroke();
      context.setLineDash([]);
    }
  }, []);

  const redraw = useCallback(() => {
    frameRef.current ??= requestAnimationFrame(draw);
  }, [draw]);

  const setView = useCallback(
    (next: MapView) => {
      viewRef.current = next;
      setPicked(null);
      setHovered(null);
      redraw();
    },
    [redraw],
  );

  useImperativeHandle(
    controllerRef,
    () => ({
      zoomBy: (factor) => {
        const view = viewRef.current;
        const { width, height } = sizeRef.current;
        if (view) setView(zoomAt(view, factor, { x: width / 2, y: height / 2 }));
      },
      reset: () =>
        setView(fitView(sizeRef.current.width, sizeRef.current.height, latest.current.bounds)),
      locate: (id) => {
        const point = latest.current.points.find((candidate) => candidate.id === id);
        const { width, height } = sizeRef.current;
        if (point) setView(centreOn(point, LOCATE_ZOOM, width, height));
      },
    }),
    [setView],
  );

  useEffect(() => {
    redraw();
  }, [points, labels, highlightGenre, nowPlayingId, selectedIds, hoveredId, redraw]);

  useEffect(() => {
    const wrap = wrapRef.current;
    const canvas = canvasRef.current;
    if (!wrap || !canvas) return;

    const resize = () => {
      const { width, height } = wrap.getBoundingClientRect();
      const ratio = window.devicePixelRatio || 1;
      const previous = sizeRef.current;

      canvas.width = Math.round(width * ratio);
      canvas.height = Math.round(height * ratio);
      sizeRef.current = { width, height };

      // Первый размер — карта целиком; дальше вид сдвигается так, чтобы середина осталась серединой.
      const view = viewRef.current;
      viewRef.current = view
        ? {
            ...view,
            panX: view.panX + (width - previous.width) / 2,
            panY: view.panY + (height - previous.height) / 2,
          }
        : fitView(width, height, latest.current.bounds);

      draw();
    };

    const observer = new ResizeObserver(resize);
    observer.observe(wrap);

    // Смена темы или палитры перекрашивает точки.
    const themes = new MutationObserver(redraw);
    themes.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ["data-theme", "data-colorway"],
    });
    const scheme = window.matchMedia("(prefers-color-scheme: dark)");
    scheme.addEventListener("change", redraw);

    const wheel = (event: WheelEvent) => {
      event.preventDefault();
      const view = viewRef.current;
      if (!view) return;

      const box = canvas.getBoundingClientRect();
      setView(
        zoomAt(view, Math.exp(-event.deltaY * 0.0015), {
          x: event.clientX - box.left,
          y: event.clientY - box.top,
        }),
      );
    };
    canvas.addEventListener("wheel", wheel, { passive: false });

    return () => {
      observer.disconnect();
      themes.disconnect();
      scheme.removeEventListener("change", redraw);
      canvas.removeEventListener("wheel", wheel);
      if (frameRef.current !== null) cancelAnimationFrame(frameRef.current);
    };
  }, [draw, redraw, setView]);

  const local = (event: { clientX: number; clientY: number }): Spot => {
    const box = canvasRef.current!.getBoundingClientRect();
    return { x: event.clientX - box.left, y: event.clientY - box.top };
  };

  const nearest = (at: Spot): MapPoint | null => {
    const view = viewRef.current;
    if (!view) return null;

    const base = Math.min(sizeRef.current.width, sizeRef.current.height);
    let best: MapPoint | null = null;
    let bestDistance = HIT_RADIUS_PX ** 2;

    for (const point of points) {
      const spot = toScreen(point, view, base);
      const distance = (spot.x - at.x) ** 2 + (spot.y - at.y) ** 2;
      if (distance < bestDistance) {
        bestDistance = distance;
        best = point;
      }
    }

    return best;
  };

  const pinchSpan = () => {
    const [a, b] = [...pointersRef.current.values()];
    return Math.hypot(a.x - b.x, a.y - b.y);
  };

  const onPointerDown = (event: React.PointerEvent<HTMLCanvasElement>) => {
    event.currentTarget.setPointerCapture(event.pointerId);
    const at = local(event);
    pointersRef.current.set(event.pointerId, at);

    if (pointersRef.current.size === 2) {
      lassoRef.current = null;
      gestureRef.current = { start: at, moved: true, pinch: pinchSpan() };
      return;
    }

    gestureRef.current = { start: at, moved: false, pinch: null };
    if (selecting) lassoRef.current = [at];
  };

  const onPointerMove = (event: React.PointerEvent<HTMLCanvasElement>) => {
    const at = local(event);
    const gesture = gestureRef.current;
    const previous = pointersRef.current.get(event.pointerId);

    if (!gesture || !previous) {
      if (event.pointerType === "mouse") {
        const point = nearest(at);
        setHovered((shown) =>
          point ? (shown?.point.id === point.id ? shown : place(point, at)) : null,
        );
      }
      return;
    }

    pointersRef.current.set(event.pointerId, at);

    if (
      !gesture.moved &&
      Math.hypot(at.x - gesture.start.x, at.y - gesture.start.y) > CLICK_SLOP_PX
    ) {
      gesture.moved = true;
    }

    const view = viewRef.current;
    if (!view || !gesture.moved) return;

    if (gesture.pinch !== null && pointersRef.current.size === 2) {
      const span = pinchSpan();
      const [a, b] = [...pointersRef.current.values()];
      setView(zoomAt(view, span / gesture.pinch, { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 }));
      gesture.pinch = span;
      return;
    }

    if (lassoRef.current) {
      lassoRef.current.push(at);
      redraw();
      return;
    }

    setView({ ...view, panX: view.panX + at.x - previous.x, panY: view.panY + at.y - previous.y });
  };

  const onPointerUp = (event: React.PointerEvent<HTMLCanvasElement>) => {
    pointersRef.current.delete(event.pointerId);
    const gesture = gestureRef.current;
    if (pointersRef.current.size > 0) return;

    gestureRef.current = null;
    const lasso = lassoRef.current;
    lassoRef.current = null;

    const at = local(event);
    const view = viewRef.current;

    if (lasso && lasso.length > 2 && gesture?.moved && view) {
      const base = Math.min(sizeRef.current.width, sizeRef.current.height);
      const polygon = lasso.map((spot) => toMap(spot, view, base));
      onSelect(points.filter((point) => pointInPolygon(point, polygon)));
      redraw();
      return;
    }

    redraw();
    if (gesture?.moved) return;

    const point = nearest(at);
    setHovered(null);
    setPicked(point ? place(point, at) : null);
  };

  const onKeyDown = (event: React.KeyboardEvent<HTMLCanvasElement>) => {
    const view = viewRef.current;
    if (!view) return;

    const { width, height } = sizeRef.current;
    const centre = { x: width / 2, y: height / 2 };
    const step = 60;

    const next: Record<string, () => MapView> = {
      "+": () => zoomAt(view, 1.4, centre),
      "=": () => zoomAt(view, 1.4, centre),
      "-": () => zoomAt(view, 1 / 1.4, centre),
      "0": () => fitView(width, height, latest.current.bounds),
      ArrowLeft: () => ({ ...view, panX: view.panX + step }),
      ArrowRight: () => ({ ...view, panX: view.panX - step }),
      ArrowUp: () => ({ ...view, panY: view.panY + step }),
      ArrowDown: () => ({ ...view, panY: view.panY - step }),
    };

    if (event.key === "Escape") {
      setPicked(null);
      return;
    }

    const move = next[event.key];
    if (!move) return;

    event.preventDefault();
    setView(move());
  };

  const card = picked ?? hovered;

  return (
    <div
      ref={wrapRef}
      className="absolute inset-0 overflow-hidden rounded-xl bg-[radial-gradient(ellipse_at_50%_45%,color-mix(in_oklab,var(--primary)_7%,var(--card)),var(--card)_75%)]"
    >
      <canvas
        ref={canvasRef}
        tabIndex={0}
        role="img"
        aria-label={t("map.region", { count: points.length })}
        className={`absolute inset-0 h-full w-full touch-none outline-none focus-visible:ring-2 focus-visible:ring-ring/40 ${
          selecting ? "cursor-crosshair" : "cursor-grab active:cursor-grabbing"
        }`}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={onPointerUp}
        onPointerLeave={() => setHovered(null)}
        onKeyDown={onKeyDown}
      />

      {card && (
        <div
          className={`absolute z-10 flex max-w-[17rem] items-center gap-2.5 rounded-lg bg-popover p-2 text-popover-foreground shadow-pop ${
            picked ? "" : "pointer-events-none"
          }`}
          style={{
            left: card.at.x,
            top: Math.max(8, card.at.y - 28),
            translate: card.flip ? "calc(-100% - 14px) 0" : "14px 0",
          }}
          role={picked ? "dialog" : undefined}
          aria-label={picked ? card.point.title : undefined}
        >
          <Cover
            albumId={card.point.albumId}
            trackId={card.point.id}
            hasCover={card.point.hasCover}
            name={card.point.title}
            size="2.5rem"
            className="shrink-0 rounded-sm"
          />

          <div className="flex min-w-0 flex-1 flex-col">
            <span className="truncate text-sm font-medium">{card.point.title}</span>
            <span className="truncate text-xs text-muted-foreground">{card.point.artist}</span>
            <span className="truncate text-xs text-faint">
              {[
                card.point.genre && genreName(card.point.genre),
                card.point.plays > 0
                  ? t("map.plays", { count: card.point.plays })
                  : t("map.neverPlayed"),
              ]
                .filter(Boolean)
                .join(" · ")}
            </span>
          </div>

          {picked && (
            <div className="flex shrink-0 flex-col gap-0.5">
              <Button
                variant="ghost"
                size="icon-sm"
                onClick={() => onPlay(picked.point)}
                aria-label={t("action.playNamed", { name: picked.point.title })}
              >
                <PlayIcon size={14} />
              </Button>
              <Button
                variant="ghost"
                size="icon-sm"
                onClick={() => onRadio(picked.point)}
                aria-label={t("map.radioFrom")}
                title={t("map.radioFrom")}
              >
                <RadioIcon size={14} />
              </Button>
            </div>
          )}

          {picked && (
            <Button
              variant="ghost"
              size="icon-sm"
              className="absolute -top-2 -right-2 size-6 bg-popover shadow-pop"
              onClick={() => setPicked(null)}
              aria-label={t("action.close")}
            >
              <XIcon size={12} />
            </Button>
          )}
        </div>
      )}
    </div>
  );
}
