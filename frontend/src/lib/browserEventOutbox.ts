// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { EventOutboxEntry, EventOutboxStorage } from "@/lib/eventOutbox";

export const EVENT_OUTBOX_DATABASE = "caimack-event-outbox-v1";

const STORE = "events";
const VERSION = 1;

export class BrowserEventOutboxStorage<T> implements EventOutboxStorage<T> {
  async add(entry: EventOutboxEntry<T>): Promise<void> {
    await this.withStore("readwrite", (store) => store.put(entry));
  }

  /** Ключ начинается с метки времени, поэтому обход по возрастанию — это и есть FIFO. */
  async list(limit = 100): Promise<EventOutboxEntry<T>[]> {
    return this.withStore("readonly", (store) => store.getAll(undefined, limit));
  }

  async count(): Promise<number> {
    return this.withStore("readonly", (store) => store.count());
  }

  async remove(ids: string[]): Promise<void> {
    if (ids.length === 0) return;

    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(STORE, "readwrite");
      const store = transaction.objectStore(STORE);
      ids.forEach((id) => store.delete(id));
      transaction.oncomplete = () => {
        database.close();
        resolve();
      };
      transaction.onerror = () => {
        database.close();
        reject(transaction.error);
      };
      transaction.onabort = transaction.onerror;
    });
  }

  private openDatabase(): Promise<IDBDatabase> {
    return new Promise((resolve, reject) => {
      const request = indexedDB.open(EVENT_OUTBOX_DATABASE, VERSION);
      request.onupgradeneeded = () => {
        if (!request.result.objectStoreNames.contains(STORE)) {
          request.result.createObjectStore(STORE, { keyPath: "id" });
        }
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }

  private async withStore<R>(
    mode: IDBTransactionMode,
    action: (store: IDBObjectStore) => IDBRequest<R>,
  ): Promise<R> {
    const database = await this.openDatabase();
    return new Promise<R>((resolve, reject) => {
      const transaction = database.transaction(STORE, mode);
      const request = action(transaction.objectStore(STORE));
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
      transaction.oncomplete = () => database.close();
      transaction.onabort = () => {
        database.close();
        reject(transaction.error);
      };
    });
  }
}
