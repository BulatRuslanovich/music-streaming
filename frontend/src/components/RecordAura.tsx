// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useRef } from "react";
import { rgbOf, rgba, type Rgb } from "@/lib/cssColor";
import { audioAnalyser } from "@/lib/playback/equalizer";
import { bandBins, bandLevel, beatPulse, follow, onset } from "@/lib/playback/spectrum";
import { usePlayerProgress } from "@/contexts/PlayerContext";

// Лучей на половину круга; вторая половина — зеркало, низы сверху, верха снизу.
const RAYS = 36;

// Холст шире пластинки на столько с каждой стороны — туда и вытягиваются лучи.
const REACH = 1.32;

const BASS_HZ = 150;
const COLOR_REFRESH_FRAMES = 60;

// «Живая» пластинка: вокруг диска — спектр того, что играет, а фон плеера дышит басом
// (переменная --beat на ближайшем [data-aura-host]). Где Web Audio нет (iOS), пульс берётся
// из темпа трека.
export function RecordAura({
  playing,
  bpm,
  offset,
}: {
  playing: boolean;
  bpm?: number | null;
  offset?: number;
}) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const { getPosition } = usePlayerProgress();
  const live = useRef({ playing, bpm, offset, getPosition });

  useEffect(() => {
    live.current = { playing, bpm, offset, getPosition };
  });

  useEffect(() => {
    const canvas = canvasRef.current;
    const context = canvas?.getContext("2d");
    if (!canvas || !context) return;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;

    const host = canvas.closest<HTMLElement>("[data-aura-host]");
    const levels = new Float32Array(RAYS);
    let bands: [number, number][] | null = null;
    let bassBins: [number, number] = [1, 2];
    let data: Uint8Array<ArrayBuffer> | null = null;
    let bass = 0;
    let bassAverage = 0;
    let beat = 0;
    let size = 0;
    let color: Rgb = [217, 164, 65];
    let frames = 0;
    let frame = 0;

    const resize = () => {
      const box = canvas.getBoundingClientRect();
      const ratio = window.devicePixelRatio || 1;
      size = box.width;
      canvas.width = Math.round(box.width * ratio);
      canvas.height = Math.round(box.height * ratio);
      context.setTransform(ratio, 0, 0, ratio, 0, 0);
    };

    const observer = new ResizeObserver(resize);
    observer.observe(canvas);

    const listen = () => {
      const { playing, bpm, offset, getPosition } = live.current;
      const analyser = playing ? audioAnalyser() : null;

      if (analyser) {
        if (!bands || !data) {
          const binHz = analyser.context.sampleRate / analyser.fftSize;
          bands = bandBins(RAYS, analyser.context.sampleRate, analyser.fftSize);
          bassBins = [1, Math.max(2, Math.round(BASS_HZ / binHz))];
          data = new Uint8Array(analyser.frequencyBinCount);
        }

        analyser.getByteFrequencyData(data);
        for (let ray = 0; ray < RAYS; ray++)
          levels[ray] = follow(levels[ray], bandLevel(data, bands[ray]));

        bass = follow(bass, bandLevel(data, bassBins));
        bassAverage += (bass - bassAverage) * 0.02;
        beat = Math.max(beat * 0.9, onset(bass, bassAverage));
        return;
      }

      if (playing && bpm) {
        const pulse = beatPulse(getPosition(), bpm, offset ?? 0);
        for (let ray = 0; ray < RAYS; ray++) {
          const shape = 0.3 + 0.3 * Math.sin(ray * 1.7) ** 2;
          levels[ray] = follow(levels[ray], pulse * shape, 0.6, 0.2);
        }
        beat = pulse * 0.6;
        return;
      }

      for (let ray = 0; ray < RAYS; ray++) levels[ray] = follow(levels[ray], 0);
      beat *= 0.9;
    };

    const draw = () => {
      context.clearRect(0, 0, size, size);
      if (size === 0) return;

      const centre = size / 2;
      const disc = centre / REACH;
      const room = centre - disc - 4;

      if (beat > 0.01) {
        const glow = context.createRadialGradient(
          centre,
          centre,
          disc * 0.9,
          centre,
          centre,
          centre,
        );
        glow.addColorStop(0, rgba(color, 0.28 * beat));
        glow.addColorStop(1, rgba(color, 0));
        context.fillStyle = glow;
        context.fillRect(0, 0, size, size);
      }

      context.lineCap = "round";
      context.lineWidth = Math.max(2, size / 200);

      for (let ray = 0; ray < RAYS; ray++) {
        const level = levels[ray];
        if (level < 0.01) continue;

        const length = 3 + level * room;
        context.strokeStyle = rgba(color, 0.35 + 0.6 * level);
        context.beginPath();

        for (const side of [1, -1]) {
          const angle = -Math.PI / 2 + side * ((ray + 0.5) / RAYS) * Math.PI;
          const cos = Math.cos(angle);
          const sin = Math.sin(angle);
          context.moveTo(centre + cos * (disc + 3), centre + sin * (disc + 3));
          context.lineTo(centre + cos * (disc + 3 + length), centre + sin * (disc + 3 + length));
        }

        context.stroke();
      }
    };

    const tick = () => {
      frame = requestAnimationFrame(tick);

      if (frames++ % COLOR_REFRESH_FRAMES === 0) {
        color = rgbOf(getComputedStyle(canvas).getPropertyValue("--primary"), color);
      }

      listen();
      host?.style.setProperty("--beat", beat.toFixed(3));
      draw();
    };
    frame = requestAnimationFrame(tick);

    return () => {
      cancelAnimationFrame(frame);
      observer.disconnect();
      host?.style.removeProperty("--beat");
    };
  }, []);

  return <canvas ref={canvasRef} className="record-aura" aria-hidden="true" />;
}
