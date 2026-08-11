"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { useT } from "./I18nContext";

type ToastTone = "info" | "success" | "error";

interface Toast {
  id: number;
  message: string;
  tone: ToastTone;
}

interface ToastState {
  notify: (message: string, tone?: ToastTone) => void;
  notifyError: (error: unknown, fallback?: string) => void;
  dismiss: (id: number) => void;
}

const ToastContext = createContext<ToastState | null>(null);

// Errors stay up for a minute so they can be read and acted on; confirmations just pass by.
const VISIBLE_MS: Record<ToastTone, number> = {
  info: 4_000,
  success: 4_000,
  error: 60_000,
};

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const t = useT();
  const [toasts, setToasts] = useState<Toast[]>([]);
  const nextId = useRef(1);

  // The ref is the source of truth so notify() can read the current stack synchronously.
  const items = useRef<Toast[]>([]);
  const timers = useRef(new Map<number, number>());

  const dismiss = useCallback((id: number) => {
    window.clearTimeout(timers.current.get(id));
    timers.current.delete(id);
    items.current = items.current.filter((toast) => toast.id !== id);
    setToasts(items.current);
  }, []);

  const notify = useCallback(
    (message: string, tone: ToastTone = "info") => {
      // The same message repeating (a failing page reloaded, say) refreshes the existing
      // toast rather than stacking copies that would each linger for a minute.
      const existing = items.current.find((toast) => toast.message === message && toast.tone === tone);
      const id = existing?.id ?? nextId.current++;

      if (existing) {
        window.clearTimeout(timers.current.get(id));
      } else {
        items.current = [...items.current, { id, message, tone }];
        setToasts(items.current);
      }

      timers.current.set(id, window.setTimeout(() => dismiss(id), VISIBLE_MS[tone]));
    },
    [dismiss],
  );

  const notifyError = useCallback(
    (error: unknown, fallback?: string) => {
      const message =
        error instanceof Error && error.message ? error.message : (fallback ?? t("error.generic"));
      notify(message, "error");
    },
    [notify, t],
  );

  useEffect(() => {
    const pending = timers.current;
    return () => {
      pending.forEach((timer) => window.clearTimeout(timer));
      pending.clear();
    };
  }, []);

  const value = useMemo<ToastState>(
    () => ({ notify, notifyError, dismiss }),
    [notify, notifyError, dismiss],
  );

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="toast-stack" role="status" aria-live="polite">
        {toasts.map((toast) => (
          <div key={toast.id} className={`toast toast-${toast.tone}`}>
            <span className="toast-message">{toast.message}</span>
            <button
              type="button"
              className="toast-dismiss"
              onClick={() => dismiss(toast.id)}
              aria-label={t("action.dismiss")}
            >
              ×
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastState {
  const context = useContext(ToastContext);
  if (!context) throw new Error("useToast must be used inside <ToastProvider>");
  return context;
}
