"use client";

import { cn } from "@/lib/cn";
import { useT } from "@/contexts/I18nContext";
import type { TranslationKey, TranslationValues } from "@/lib/i18n";

export interface ActivityPoint {
  key: string;
  label: string;
  value: number;
  plays: number;
  tick?: string;
}

const NICE_STEPS = [60, 120, 300, 600, 900, 1800, 3600, 7200, 10800, 21600, 43200, 86400];

const MAX_TICK_INTERVALS = 4;

const DENSE_FROM = 90;

export function ActivityChart({
  points,
  columnLabel,
  tableLabel,
  formatValue,
}: {
  points: ActivityPoint[];
  columnLabel: string;
  tableLabel: string;
  formatValue: (seconds: number) => string;
}) {
  const t = useT();

  if (points.length === 0) return null;

  const { top, ticks } = scaleFor(Math.max(...points.map((point) => point.value)));
  const dense = points.length > DENSE_FROM;

  return (
    <figure className="m-0 flex flex-col">
      <div className="grid grid-cols-[4rem_minmax(0,1fr)] gap-x-2 pt-10">
        <div aria-hidden="true" className="relative h-40">
          {ticks.map((tick) => (
            <span
              key={tick}
              style={{ bottom: `${(tick / top) * 100}%` }}
              className="absolute right-0 translate-y-1/2 text-2xs whitespace-nowrap text-faint tabular-nums"
            >
              {tickLabel(tick, t)}
            </span>
          ))}
        </div>

        <div aria-hidden="true" className="relative h-40">
          {ticks.map((tick) => (
            <span
              key={tick}
              style={{ bottom: `${(tick / top) * 100}%` }}
              className={cn(
                "absolute inset-x-0 h-px",
                tick === 0 ? "bg-border-strong" : "bg-border",
              )}
            />
          ))}

          <ol className="absolute inset-0 flex items-end">
            {points.map((point, index) => (
              <li
                key={point.key}
                className={cn("group relative flex h-full flex-1 items-end", !dense && "px-px")}
              >
                <span
                  style={{ height: `${heightOf(point.value, top)}%` }}
                  className="relative flex w-full justify-center"
                >
                  <span
                    className={cn(
                      "block h-full w-full max-w-6 rounded-t-[4px] bg-primary opacity-80 transition-opacity",
                      "group-hover:opacity-100",
                      point.value > 0 && "min-h-0.5",
                    )}
                  />

                  <span
                    className={cn(
                      "pointer-events-none absolute bottom-full z-10 mb-1.5 hidden w-max items-baseline gap-1.5",
                      "rounded-lg border border-border-strong bg-popover px-2.5 py-1 whitespace-nowrap",
                      "text-popover-foreground shadow-pop group-hover:flex",
                      anchorFor(index, points.length),
                    )}
                  >
                    <span className="text-sm font-semibold tabular-nums">
                      {formatValue(point.value)}
                    </span>
                    <span className="text-2xs text-muted-foreground">
                      {point.label} · {t("stats.playCount", { count: point.plays })}
                    </span>
                  </span>
                </span>
              </li>
            ))}
          </ol>
        </div>

        <div />

        <ol aria-hidden="true" className="flex pt-1.5">
          {points.map((point) => (
            <li key={point.key} className="relative h-4 min-w-px flex-1">
              {point.tick && (
                <span className="absolute left-1/2 -translate-x-1/2 text-2xs whitespace-nowrap text-faint tabular-nums">
                  {point.tick}
                </span>
              )}
            </li>
          ))}
        </ol>
      </div>

      <table className="sr-only">
        <caption>{tableLabel}</caption>
        <thead>
          <tr>
            <th scope="col">{columnLabel}</th>
            <th scope="col">{t("stats.listeningTime")}</th>
            <th scope="col">{t("stats.plays")}</th>
          </tr>
        </thead>
        <tbody>
          {points.map((point) => (
            <tr key={point.key}>
              <th scope="row">{point.label}</th>
              <td>{formatValue(point.value)}</td>
              <td>{point.plays}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </figure>
  );
}

function scaleFor(max: number): { top: number; ticks: number[] } {
  if (max <= 0) return { top: 1, ticks: [0] };

  const step =
    NICE_STEPS.find((candidate) => max / candidate <= MAX_TICK_INTERVALS) ??
    NICE_STEPS[NICE_STEPS.length - 1];

  const top = Math.ceil(max / step) * step;
  const ticks: number[] = [];

  for (let value = 0; value <= top; value += step) ticks.push(value);

  return { top, ticks };
}

function tickLabel(
  seconds: number,
  t: (key: TranslationKey, values?: TranslationValues) => string,
) {
  if (seconds <= 0) return "0";
  if (seconds < 3600) return t("unit.minutes", { count: seconds / 60 });

  const hours = Math.floor(seconds / 3600);
  const minutes = (seconds % 3600) / 60;

  return minutes === 0
    ? t("unit.hours", { count: hours })
    : t("unit.hoursMinutes", { hours, minutes });
}

function heightOf(value: number, top: number): number {
  if (value <= 0 || top <= 0) return 0;
  return Math.min(100, (value / top) * 100);
}

function anchorFor(index: number, count: number): string {
  if (count <= 2) return "left-1/2 -translate-x-1/2";
  if (index <= (count - 1) * 0.15) return "left-0";
  if (index >= (count - 1) * 0.85) return "right-0";
  return "left-1/2 -translate-x-1/2";
}
