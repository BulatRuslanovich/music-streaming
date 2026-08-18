"use client";

import type { UseQueryResult } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useT } from "@/contexts/I18nContext";
import { Button } from "./ui/button";
import { SkeletonGroup } from "./ui/skeleton";
import { EmptyState } from "./EmptyState";

export function LoadError({ message, onRetry }: { message: string; onRetry?: () => void }) {
  const t = useT();

  return (
    <div
      role="alert"
      className="flex flex-wrap items-center gap-3 rounded-lg border border-destructive/40 bg-destructive/10 px-5 py-3"
    >
      <p className="flex-1">{message}</p>
      {onRetry && (
        <Button variant="outline" size="sm" onClick={onRetry}>
          {t("action.tryAgain")}
        </Button>
      )}
    </div>
  );
}

interface EmptyCopy {
  title: string;
  description?: string;
  action?: ReactNode;
}

function looksEmpty(data: unknown): boolean {
  if (Array.isArray(data)) return data.length === 0;

  if (data && typeof data === "object" && "items" in data) {
    const paged = data as { items: unknown[]; total?: number };
    return (paged.total ?? paged.items.length) === 0;
  }

  return false;
}

export function Query<T>({
  result,
  skeleton = "card",
  skeletonCount = 8,
  empty,
  isEmpty = looksEmpty,
  children,
}: {
  result: UseQueryResult<T>;
  skeleton?: "card" | "row" | "tile";
  skeletonCount?: number;
  empty?: EmptyCopy;
  isEmpty?: (data: T) => boolean;
  children: (data: T) => ReactNode;
}) {
  const t = useT();
  const { data, error, isPending, refetch } = result;

  if (error && data === undefined) {
    return (
      <LoadError
        message={error instanceof Error ? error.message : t("error.load")}
        onRetry={() => void refetch()}
      />
    );
  }

  if (isPending) return <SkeletonGroup variant={skeleton} count={skeletonCount} />;
  if (data === undefined) return null;

  if (empty && isEmpty(data)) {
    return <EmptyState title={empty.title} description={empty.description} action={empty.action} />;
  }

  return <>{children(data)}</>;
}
