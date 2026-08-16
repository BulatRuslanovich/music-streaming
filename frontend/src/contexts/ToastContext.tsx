"use client";

import { useCallback, useMemo, type ReactNode } from "react";
import { toast } from "sonner";
import { Toaster } from "@/components/ui/sonner";
import { useT } from "./I18nContext";

type ToastTone = "info" | "success" | "error";

interface ToastState {
  notify: (message: string, tone?: ToastTone) => void;
  notifyError: (error: unknown, fallback?: string) => void;
}

/*
 * Ошибка висит минуту, всё остальное — четыре секунды: сообщение об успехе можно и
 * пропустить, а причину отказа читают тогда, когда заметят, что ничего не произошло.
 */
const VISIBLE_MS: Record<ToastTone, number> = {
  info: 4_000,
  success: 4_000,
  error: 60_000,
};

/**
 * Стек и анимацию отдали sonner, а эта обёртка осталась ради notify/notifyError: их зовут
 * из двух десятков мест, и переписывать каждое ради смены библиотеки было бы нечестно.
 */
export function ToastProvider({ children }: { children: ReactNode }) {
  return (
    <>
      {children}
      <Toaster />
    </>
  );
}

export function useToast(): ToastState {
  const t = useT();

  const notify = useCallback((message: string, tone: ToastTone = "info") => {
    // Идентификатор из самого сообщения: повтор обновляет показанное, а не громоздит копии.
    const options = { id: `${tone}:${message}`, duration: VISIBLE_MS[tone] };

    if (tone === "success") toast.success(message, options);
    else if (tone === "error") toast.error(message, options);
    else toast(message, options);
  }, []);

  const notifyError = useCallback(
    (error: unknown, fallback?: string) => {
      const message =
        error instanceof Error && error.message ? error.message : (fallback ?? t("error.generic"));
      notify(message, "error");
    },
    [notify, t],
  );

  return useMemo(() => ({ notify, notifyError }), [notify, notifyError]);
}
