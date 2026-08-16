"use client";

import type { ReactNode } from "react";
import type { FieldValues, SubmitHandler, UseFormReturn } from "react-hook-form";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { Button } from "./ui/button";
import { Dialog, DialogContent } from "./ui/dialog";

/**
 * Диалог с формой. Забирает на себя то, что раньше повторялось в каждом из четырёх диалогов:
 * состояние отправки, try/catch вокруг запроса, сообщение об успехе и об отказе, закрытие
 * после сохранения и футер с двумя кнопками.
 *
 * Диалог не закрывается, пока запрос не ответил: иначе отказ сервера показался бы тостом
 * над уже пустым экраном, и введённое было бы потеряно.
 */
export function FormDialog<TInput extends FieldValues, TOutput extends FieldValues = TInput>({
  title,
  description,
  form,
  onSubmit,
  onClose,
  successMessage,
  errorMessage,
  submitLabel,
  pendingLabel,
  children,
}: {
  title: string;
  description?: string;
  /* Вход и выход формы различаются, когда схема что-то преобразует: строка из поля — в число. */
  form: UseFormReturn<TInput, unknown, TOutput>;
  onSubmit: SubmitHandler<TOutput>;
  onClose: () => void;
  successMessage?: string;
  errorMessage: string;
  submitLabel: string;
  pendingLabel: string;
  children: ReactNode;
}) {
  const t = useT();
  const { notify, notifyError } = useToast();

  const saving = form.formState.isSubmitting;

  const submit = form.handleSubmit(async (values) => {
    try {
      await onSubmit(values);
      if (successMessage) notify(successMessage, "success");
      onClose();
    } catch (reason) {
      notifyError(reason, errorMessage);
    }
  });

  return (
    <Dialog open onOpenChange={(open) => !open && !saving && onClose()}>
      <DialogContent
        title={title}
        description={description}
        footer={
          <>
            <Button variant="primary" form="form-dialog" type="submit" disabled={saving}>
              {saving ? pendingLabel : submitLabel}
            </Button>
            <Button variant="outline" onClick={onClose} disabled={saving}>
              {t("action.cancel")}
            </Button>
          </>
        }
      >
        <form
          id="form-dialog"
          onSubmit={(event) => void submit(event)}
          className="flex flex-col gap-4"
        >
          {children}
        </form>
      </DialogContent>
    </Dialog>
  );
}
