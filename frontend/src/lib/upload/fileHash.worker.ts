// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { sha256File } from "./fileHash";

interface HashRequest {
  id: number;
  file: File;
}

interface HashResponse {
  id: number;
  hash?: string;
}

self.addEventListener("message", (event: MessageEvent<HashRequest>) => {
  const { id, file } = event.data;

  void sha256File(file)
    .then((hash) => self.postMessage({ id, hash } satisfies HashResponse))
    .catch(() => self.postMessage({ id } satisfies HashResponse));
});
