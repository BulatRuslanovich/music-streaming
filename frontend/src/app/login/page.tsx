// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { signInSchema, type SignInValues } from "@/lib/schemas";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { Copyright } from "@/components/Copyright";
import { BrandMark, BrandWordmark } from "@/components/Brand";
import { LocaleSwitcher } from "@/components/LocaleSwitcher";
import { ThemeSwitcher } from "@/components/ThemeSwitcher";
import { Button } from "@/components/ui/button";
import { Surface } from "@/components/ui/card";
import { TextField } from "@/components/ui/form";

export default function LoginPage() {
  const { signIn } = useAuth();
  const { notifyError } = useToast();
  const t = useT();
  const router = useRouter();

  const form = useForm<SignInValues>({
    resolver: zodResolver(signInSchema),
    defaultValues: { username: "", password: "" },
  });

  const submit = form.handleSubmit(async ({ username, password }) => {
    try {
      await signIn(username, password);
      router.replace("/");
    } catch (reason) {
      notifyError(reason, t("auth.failed"));
      form.setValue("password", "");
    }
  });

  const submitting = form.formState.isSubmitting;

  return (
    <div className="relative grid min-h-dvh justify-items-center bg-background p-6 [align-items:safe_center]">
      <div className="absolute top-4 right-4 flex items-center gap-1">
        <LocaleSwitcher />
        <ThemeSwitcher />
      </div>

      <Surface padding="lg" className="w-[min(24rem,100%)]">
        <form onSubmit={(event) => void submit(event)} className="flex flex-col gap-4" noValidate>
          <div className="mb-2 flex flex-col items-center gap-2 text-center">
            <BrandMark className="size-18" />
            <h1>
              <BrandWordmark className="text-xl" />
            </h1>
            <p className="text-sm text-muted-foreground">{t("auth.tagline")}</p>
          </div>

          <TextField
            label={t("field.username")}
            id="username"
            registration={form.register("username")}
            error={form.formState.errors.username && t("form.required")}
            autoComplete="username"
            autoCapitalize="none"
            spellCheck={false}
            disabled={submitting}
          />

          <TextField
            label={t("field.password")}
            id="password"
            type="password"
            registration={form.register("password")}
            error={form.formState.errors.password && t("form.required")}
            autoComplete="current-password"
            disabled={submitting}
          />

          <Button variant="primary" type="submit" className="mt-2" disabled={submitting}>
            {submitting ? t("auth.signingIn") : t("auth.signIn")}
          </Button>
        </form>
      </Surface>

      <Copyright className="absolute inset-x-0 bottom-4 text-center" />
    </div>
  );
}
