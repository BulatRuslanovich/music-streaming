// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { OfflineRecord, OfflineStorageAdapter } from "@/lib/offline/offlineLibrary";

export const OFFLINE_MEDIA_CACHE = "caimack-offline-media-v1";
export const OFFLINE_DATABASE = "caimack-offline-v1";

const STORE = "tracks";
const USER_INDEX = "by-user";
const VERSION = 1;

export class BrowserOfflineStorage implements OfflineStorageAdapter {
  async load(userId: string): Promise<OfflineRecord[]> {
    return this.withStore("readonly", (store) =>
      store.index(USER_INDEX).getAll(IDBKeyRange.only(userId)),
    );
  }

  async save(record: OfflineRecord): Promise<void> {
    await this.withStore("readwrite", (store) => store.put(record));
  }

  async remove(record: OfflineRecord): Promise<void> {
    await this.dropResources(record.resourceUrls);
    await this.withStore("readwrite", (store) => store.delete([record.userId, record.track.id]));
  }

  async dropResources(urls: string[]): Promise<void> {
    if (urls.length === 0) return;

    const cache = await caches.open(OFFLINE_MEDIA_CACHE);
    await Promise.all(urls.map((url) => cache.delete(url, { ignoreVary: true })));
  }

  async hasResource(url: string): Promise<boolean> {
    const cache = await caches.open(OFFLINE_MEDIA_CACHE);
    return (await cache.match(url, { ignoreVary: true })) !== undefined;
  }

  async putResource(url: string, response: Response): Promise<number> {
    const cache = await caches.open(OFFLINE_MEDIA_CACHE);
    const measured = response.clone();
    await cache.put(url, response);

    const fromHeader = Number(measured.headers.get("Content-Length") ?? 0);
    return fromHeader > 0 ? fromHeader : (await measured.arrayBuffer()).byteLength;
  }

  async requestPersistence(): Promise<boolean> {
    if (!navigator.storage?.persist) return false;
    return navigator.storage.persist();
  }

  private openDatabase(): Promise<IDBDatabase> {
    return new Promise((resolve, reject) => {
      const request = indexedDB.open(OFFLINE_DATABASE, VERSION);
      request.onupgradeneeded = () => {
        const database = request.result;
        const store = database.objectStoreNames.contains(STORE)
          ? request.transaction!.objectStore(STORE)
          : database.createObjectStore(STORE, { keyPath: ["userId", "track.id"] });

        if (!store.indexNames.contains(USER_INDEX)) store.createIndex(USER_INDEX, "userId");
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }

  private async withStore<T>(
    mode: IDBTransactionMode,
    action: (store: IDBObjectStore) => IDBRequest<T>,
  ): Promise<T> {
    const database = await this.openDatabase();
    return new Promise<T>((resolve, reject) => {
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
