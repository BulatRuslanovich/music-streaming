import type { Track } from "@/lib/types";

export interface QueueShape {
  queue: Track[];
  order: number[];
}

export type Advance =
  { kind: "none" } | { kind: "restart" } | { kind: "stop" } | { kind: "play"; index: number };

export function buildOrder(
  length: number,
  shuffled: boolean,
  startIndex: number,
  random: () => number = Math.random,
): number[] {
  const indices = Array.from({ length }, (_, index) => index);
  if (!shuffled) return indices;

  for (let i = indices.length - 1; i > 0; i -= 1) {
    const j = Math.floor(random() * (i + 1));
    [indices[i], indices[j]] = [indices[j], indices[i]];
  }

  if (startIndex >= 0) {
    const at = indices.indexOf(startIndex);
    if (at > 0) [indices[0], indices[at]] = [indices[at], indices[0]];
  }

  return indices;
}

export function appendTrack(queue: Track[], order: number[], track: Track): QueueShape {
  const next = [...queue, track];
  return { queue: next, order: [...order, next.length - 1] };
}

export function appendTracks(queue: Track[], order: number[], tracks: Track[]): QueueShape {
  return {
    queue: [...queue, ...tracks],
    order: [...order, ...tracks.map((_, offset) => queue.length + offset)],
  };
}

export function insertAfter(
  queue: Track[],
  order: number[],
  currentIndex: number,
  track: Track,
): QueueShape {
  const at = currentIndex + 1;
  const next = [...queue.slice(0, at), track, ...queue.slice(at)];
  const shifted = order.map((index) => (index >= at ? index + 1 : index));

  shifted.splice(shifted.indexOf(currentIndex) + 1, 0, at);

  return { queue: next, order: shifted };
}

export function radioStartAfterInsert(
  radioFrom: number,
  insertedAt: number,
  queueLength: number,
): number {
  return radioFrom < queueLength && insertedAt <= radioFrom ? radioFrom + 1 : radioFrom;
}

export function indexAfterRemoval(
  removedIndex: number,
  activeIndex: number,
  remainingLength: number,
): number {
  if (remainingLength === 0) return -1;
  if (removedIndex < activeIndex) return activeIndex - 1;
  if (removedIndex === activeIndex) return Math.min(activeIndex, remainingLength - 1);
  return activeIndex;
}

export function advanceIn(
  order: number[],
  currentIndex: number,
  direction: 1 | -1,
  repeatAll: boolean,
): Advance {
  if (order.length === 0 || currentIndex < 0) return { kind: "none" };

  const positionInOrder = order.indexOf(currentIndex);
  if (positionInOrder === -1) return { kind: "none" };

  const nextPosition = positionInOrder + direction;

  if (nextPosition < 0) return { kind: "restart" };

  if (nextPosition >= order.length) {
    return repeatAll ? { kind: "play", index: order[0] } : { kind: "stop" };
  }

  return { kind: "play", index: order[nextPosition] };
}
