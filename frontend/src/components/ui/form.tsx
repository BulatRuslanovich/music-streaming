// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useId, type ComponentProps, type ReactNode } from "react";
import {
  Controller,
  type Control,
  type FieldPath,
  type FieldValues,
  type UseFormRegisterReturn,
} from "react-hook-form";
import { cn } from "@/lib/cn";
import { Checkbox } from "./checkbox";
import { Input } from "./input";
import { Label } from "./label";
import { Textarea } from "./textarea";

export function Field({
  label,
  hint,
  error,
  htmlFor,
  className,
  children,
}: {
  label?: ReactNode;
  hint?: ReactNode;
  error?: string;
  htmlFor?: string;
  className?: string;
  children: ReactNode;
}) {
  return (
    <div className={cn("flex flex-col gap-1.5", className)}>
      {label && <Label htmlFor={htmlFor}>{label}</Label>}
      {children}
      {error ? (
        <p className="text-sm text-destructive">{error}</p>
      ) : (
        hint && <p className="text-sm text-muted-foreground">{hint}</p>
      )}
    </div>
  );
}

export function TextField({
  label,
  hint,
  error,
  registration,
  className,
  id,
  ...props
}: Omit<ComponentProps<typeof Input>, "name"> & {
  label?: ReactNode;
  hint?: ReactNode;
  error?: string;
  registration: UseFormRegisterReturn;
  className?: string;
}) {
  const generated = useId();
  const fieldId = id ?? generated;

  return (
    <Field label={label} hint={hint} error={error} htmlFor={fieldId} className={className}>
      <Input id={fieldId} aria-invalid={error ? true : undefined} {...registration} {...props} />
    </Field>
  );
}

export function TextAreaField({
  label,
  hint,
  error,
  registration,
  className,
  id,
  ...props
}: Omit<ComponentProps<typeof Textarea>, "name"> & {
  label?: ReactNode;
  hint?: ReactNode;
  error?: string;
  registration: UseFormRegisterReturn;
  className?: string;
}) {
  const generated = useId();
  const fieldId = id ?? generated;

  return (
    <Field label={label} hint={hint} error={error} htmlFor={fieldId} className={className}>
      <Textarea id={fieldId} aria-invalid={error ? true : undefined} {...registration} {...props} />
    </Field>
  );
}

export function CheckboxField<T extends FieldValues>({
  control,
  name,
  label,
  hint,
}: {
  control: Control<T>;
  name: FieldPath<T>;
  label: ReactNode;
  hint?: ReactNode;
}) {
  const id = useId();

  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-center gap-2.5">
        <Controller
          control={control}
          name={name}
          render={({ field }) => (
            <Checkbox
              id={id}
              checked={Boolean(field.value)}
              onCheckedChange={(checked) => field.onChange(checked === true)}
              onBlur={field.onBlur}
              ref={field.ref}
            />
          )}
        />
        <Label htmlFor={id} className="font-normal text-foreground">
          {label}
        </Label>
      </div>
      {hint && <p className="text-sm text-muted-foreground">{hint}</p>}
    </div>
  );
}
