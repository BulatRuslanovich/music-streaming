"use client";

import { useRouter } from "next/navigation";
import { ReactNode, useEffect } from "react";
import { useAuth } from "@/contexts/AuthContext";
import { SkeletonGroup } from "@/components/ui/skeleton";

/**
 * В админке остались только пользователи: исполнителей и треки правят и удаляют там, где их
 * слушают, отдельные списки-дубли для этого не нужны. Разделов нет — нет и вкладок, layout здесь
 * ради одной вещи: закрыть весь /admin от тех, кому туда нельзя.
 */
export default function AdminLayout({ children }: { children: ReactNode }) {
  const { isAdmin, loading } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (!loading && !isAdmin) router.replace("/");
  }, [loading, isAdmin, router]);

  if (loading) return <SkeletonGroup variant="row" count={6} />;
  if (!isAdmin) return null;

  return <>{children}</>;
}
