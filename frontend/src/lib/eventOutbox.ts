// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export interface EventOutboxEntry<T> {
  id: string;
  payload: T;
}

export interface EventOutboxStorage<T> {
  add(entry: EventOutboxEntry<T>): Promise<void>;
  /** Отдаёт записи в порядке появления — ради этого идентификаторы и монотонны. */
  list(limit?: number): Promise<EventOutboxEntry<T>[]>;
  remove(ids: string[]): Promise<void>;
  count(): Promise<number>;
}

interface EventOutboxOptions<T> {
  storage: EventOutboxStorage<T>;
  send: (events: T[]) => Promise<boolean>;
  isOnline: () => boolean;
  createId?: () => string;
  batchSize?: number;
  capacity?: number;
}

/**
 * Время в начале ключа: IndexedDB обходит записи по возрастанию ключа, так что события и
 * отправляются, и вытесняются в том порядке, в каком случились. Случайный хвост разводит те,
 * что попали в одну миллисекунду. `padStart` держит сортировку верной и после того, как
 * метка времени в base36 подрастёт на разряд.
 */
function monotonicId(): string {
  return `${Date.now().toString(36).padStart(9, "0")}-${crypto.randomUUID()}`;
}

export interface EventOutbox<T> {
  add(event: T): Promise<void>;
  flush(): Promise<boolean>;
}

export function createEventOutbox<T>({
  storage,
  send,
  isOnline,
  createId = monotonicId,
  batchSize = 100,
  capacity = 5_000,
}: EventOutboxOptions<T>): EventOutbox<T> {
  let writes = Promise.resolve();
  let flushing: Promise<boolean> | null = null;

  return {
    add(event) {
      const write = writes.then(async () => {
        await storage.add({ id: createId(), payload: event });

        // Офлайн-сессия может тянуться сутками, а девать события некуда. Потолок держит
        // хранилище конечным и жертвует самыми старыми: свежая история слушателя полезнее
        // для рекомендаций, чем позавчерашняя, которую всё равно уже не догнать.
        const overflow = (await storage.count()) - capacity;
        if (overflow > 0) {
          await storage.remove((await storage.list(overflow)).map((entry) => entry.id));
        }
      });

      writes = write.catch(() => {});
      return write;
    },

    flush() {
      if (flushing) return flushing;

      flushing = (async () => {
        await writes;
        if (!isOnline()) return false;

        for (;;) {
          const entries = await storage.list(batchSize);
          if (entries.length === 0) return true;
          if (!(await send(entries.map((entry) => entry.payload)))) return false;

          await storage.remove(entries.map((entry) => entry.id));
          if (entries.length < batchSize) return true;
          if (!isOnline()) return false;
        }
      })().finally(() => {
        flushing = null;
      });

      return flushing;
    },
  };
}
