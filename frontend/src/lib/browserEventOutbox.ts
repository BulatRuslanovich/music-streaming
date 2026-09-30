// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { EventOutboxEntry, EventOutboxStorage } from "@/lib/eventOutbox";

const DATABASE = "caimack-event-outbox-v1";
const STORE = "events";
const VERSION = 1;

export class BrowserEventOutboxStorage<T> implements EventOutboxStorage<T> {
  async add(entry: EventOutboxEntry<T>): Promise<void> {
    await transact("readwrite", (store) => store.put(entry));
  }

  /** Ключ начинается с метки времени, поэтому обход по возрастанию — это и есть FIFO. */
  list(limit: number): Promise<EventOutboxEntry<T>[]> {
    return transact("readonly", (store) => store.getAll(undefined, limit));
  }

  count(): Promise<number> {
    return transact("readonly", (store) => store.count());
  }

  async remove(ids: string[]): Promise<void> {
    if (ids.length === 0) return;
    await transact("readwrite", (store) => ids.forEach((id) => store.delete(id)));
  }
}

/**
 * Одна транзакция над хранилищем. Соединение открывается на операцию и закрывается по её
 * завершении: держать его открытым значило бы блокировать `onupgradeneeded` в других вкладках.
 * Ошибка берётся у транзакции, а не у запроса: когда транзакция рушится целиком, у отдельного
 * запроса ошибки может не быть вовсе.
 */
async function transact<R>(
  mode: IDBTransactionMode,
  action: (store: IDBObjectStore) => IDBRequest<R> | void,
): Promise<R> {
  const database = await openDatabase();

  return new Promise<R>((resolve, reject) => {
    const transaction = database.transaction(STORE, mode);
    const request = action(transaction.objectStore(STORE));

    transaction.oncomplete = () => {
      database.close();
      resolve(request?.result as R);
    };
    transaction.onabort = () => {
      database.close();
      reject(transaction.error);
    };
  });
}

function openDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DATABASE, VERSION);
    request.onupgradeneeded = () => {
      if (!request.result.objectStoreNames.contains(STORE)) {
        request.result.createObjectStore(STORE, { keyPath: "id" });
      }
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}
