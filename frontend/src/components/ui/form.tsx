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

const labelClass = "text-sm leading-none font-medium text-muted-foreground select-none";

const controlClass = cn(
  "flex w-full rounded-md border border-control-border bg-raised px-3 py-2 text-base transition-colors outline-none",
  "placeholder:text-faint",
  "autofill:shadow-[inset_0_0_0_100px_var(--surface-raised)] autofill:[-webkit-text-fill-color:var(--foreground)]",
  "focus-visible:border-ring focus-visible:ring-2 focus-visible:ring-ring/25",
  "aria-invalid:border-destructive aria-invalid:ring-destructive/25",
  "disabled:cursor-not-allowed disabled:opacity-50",
);

interface FieldProps {
  id?: string;
  label?: ReactNode;
  hint?: ReactNode;
  error?: string;
  registration: UseFormRegisterReturn;
  className?: string;
}

function Field({
  id,
  label,
  hint,
  error,
  className,
  children,
}: Omit<FieldProps, "registration"> & { children: (id: string) => ReactNode }) {
  const generated = useId();
  const fieldId = id ?? generated;

  return (
    <div className={cn("flex flex-col gap-1.5", className)}>
      {label && (
        <label htmlFor={fieldId} className={labelClass}>
          {label}
        </label>
      )}
      {children(fieldId)}
      {error ? (
        <p className="text-sm text-destructive">{error}</p>
      ) : (
        hint && <p className="text-sm text-muted-foreground">{hint}</p>
      )}
    </div>
  );
}

export function TextField({
  id,
  label,
  hint,
  error,
  registration,
  className,
  ...props
}: Omit<ComponentProps<"input">, "name" | "id"> & FieldProps) {
  return (
    <Field id={id} label={label} hint={hint} error={error} className={className}>
      {(fieldId) => (
        <input
          id={fieldId}
          aria-invalid={error ? true : undefined}
          className={cn(controlClass, "h-10")}
          {...registration}
          {...props}
        />
      )}
    </Field>
  );
}

export function TextAreaField({
  id,
  label,
  hint,
  error,
  registration,
  className,
  ...props
}: Omit<ComponentProps<"textarea">, "name" | "id"> & FieldProps) {
  return (
    <Field id={id} label={label} hint={hint} error={error} className={className}>
      {(fieldId) => (
        <textarea
          id={fieldId}
          aria-invalid={error ? true : undefined}
          className={controlClass}
          {...registration}
          {...props}
        />
      )}
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
        <label htmlFor={id} className={cn(labelClass, "font-normal text-foreground")}>
          {label}
        </label>
      </div>
      {hint && <p className="text-sm text-muted-foreground">{hint}</p>}
    </div>
  );
}
