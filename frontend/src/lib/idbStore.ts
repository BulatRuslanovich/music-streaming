// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

/**
 * Одна транзакция над одним хранилищем IndexedDB. Соединение открывается на операцию и
 * закрывается по её завершении: держать его открытым значило бы блокировать `onupgradeneeded`
 * в других вкладках.
 *
 * `onabort` резолвится через `transaction.error`, а не через `request.error`: когда транзакция
 * рушится целиком, у отдельного запроса ошибки может не быть вовсе.
 */
export async function withStore<T>(
  openDatabase: () => Promise<IDBDatabase>,
  storeName: string,
  mode: IDBTransactionMode,
  action: (store: IDBObjectStore) => IDBRequest<T>,
): Promise<T> {
  const database = await openDatabase();

  return new Promise<T>((resolve, reject) => {
    const transaction = database.transaction(storeName, mode);
    const request = action(transaction.objectStore(storeName));

    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
    transaction.oncomplete = () => database.close();
    transaction.onabort = () => {
      database.close();
      reject(transaction.error);
    };
  });
}
