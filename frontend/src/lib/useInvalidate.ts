"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";
import { invalidates } from "@/lib/queries";

/**
 * Помечает область данных устаревшей. Прежде каждая страница передавала вниз свой reload(),
 * и компонент, менявший трек, должен был знать, кого именно позвать; теперь он называет
 * область, а перезапросом занимается кэш.
 */
export function useInvalidate(): (...scopes: (keyof typeof invalidates)[]) => void {
  const client = useQueryClient();

  return useCallback(
    (...scopes: (keyof typeof invalidates)[]) => {
      for (const scope of scopes) {
        for (const queryKey of invalidates[scope]) {
          void client.invalidateQueries({ queryKey });
        }
      }
    },
    [client],
  );
}
