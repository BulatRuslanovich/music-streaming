"use client";

import { useCallback, useState } from "react";

/**
 * Номер страницы, который сам сбрасывается на первую, когда меняется фильтр или сортировка.
 * Без этого поиск, выполненный со второй страницы, показывал бы пустой список: страница
 * осталась бы второй, а результатов стало бы меньше, чем на одну.
 */
export function usePage(deps: unknown[]): [number, (page: number) => void] {
  const key = JSON.stringify(deps);
  const [state, setState] = useState({ key, page: 1 });

  if (state.key !== key) setState({ key, page: 1 });

  const setPage = useCallback((page: number) => setState((current) => ({ ...current, page })), []);

  return [state.key === key ? state.page : 1, setPage];
}
