// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useCallback, useEffect, useImperativeHandle, useRef, useState, type Ref } from "react";
import {
  centreOn,
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

interface Placed {
  point: MapPoint;
  at: Spot;
  flip: boolean;
}

interface Palette {
  primary: Rgb;
  faint: Rgb;
  foreground: string;
  card: string;
  font: string;
}

function readPalette(element: Element): Palette {
  const style = getComputedStyle(element);
  const read = (name: string) => style.getPropertyValue(name);

  return {
    primary: rgbOf(read("--primary")),
    faint: rgbOf(read("--faint-foreground")),
    foreground: read("--foreground").trim() || "#888",
    card: read("--card").trim() || "#000",
    font: style.fontFamily,
  };
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

  const latest = useRef({ points, labels, highlightGenre, nowPlayingId, selectedIds, maxPlays });
  useEffect(() => {
    latest.current = { points, labels, highlightGenre, nowPlayingId, selectedIds, maxPlays };
  });

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

    const { points, labels, highlightGenre, nowPlayingId, selectedIds, maxPlays } = latest.current;
    const palette = readPalette(canvas);
    const ratio = window.devicePixelRatio || 1;
    const base = Math.min(width, height);
    const radius = dotRadius(view.k);

    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    context.clearRect(0, 0, width, height);

    // true/false — точка в фокусе (выделение или жанр) или в фоне; null — фокуса нет.
    const emphasised = (point: MapPoint) =>
      selectedIds
        ? selectedIds.has(point.id)
        : highlightGenre
          ? point.genre === highlightGenre
          : null;

    // Точки раскладываются по корзинам цвета, чтобы не менять fillStyle на каждой.
    const buckets = new Map<string, Spot[]>();
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

    for (const [fill, spots] of buckets) {
      context.fillStyle = fill;
      dots(spots, radius);
    }

    if (front.length > 0) {
      context.fillStyle = rgba(palette.primary, 1);
      dots(front, radius + 0.5);
    }

    context.lineWidth = 2;
    context.strokeStyle = palette.foreground;

    if (current) {
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

    context.textAlign = "center";
    context.textBaseline = "middle";
    context.lineJoin = "round";

    for (const label of labels) {
      const spot = toScreen(label, view, base);
      context.font = `${label.strong ? 700 : 600} ${label.strong ? 15 : 13}px ${palette.font}`;
      context.lineWidth = 4;
      context.strokeStyle = palette.card;
      context.strokeText(label.text, spot.x, spot.y);
      context.fillStyle = palette.foreground;
      context.fillText(label.text, spot.x, spot.y);
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
      context.strokeStyle = palette.foreground;
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
      reset: () => setView(fitView(sizeRef.current.width, sizeRef.current.height)),
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
        : fitView(width, height);

      draw();
    };

    const observer = new ResizeObserver(resize);
    observer.observe(wrap);

    // Смена темы перекрашивает точки.
    const themes = new MutationObserver(redraw);
    themes.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
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
      "0": () => fitView(width, height),
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
    <div ref={wrapRef} className="relative h-full w-full overflow-hidden rounded-lg bg-card">
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
                card.point.genre,
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
