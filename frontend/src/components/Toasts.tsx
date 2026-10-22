"use client";

import Link from "next/link";
import { createContext, useCallback, useContext, useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { Icon } from "@/components/icons";
import type { IconName } from "@/components/icons";
import { tones } from "@/components/ui";
import type { Tone } from "@/components/ui";

export interface Toast {
  // A toast with the same key replaces the previous one, so a title's "searching" note becomes its result.
  key: string;
  icon: IconName;
  tone: Tone;
  title: string;
  message?: string | null;
  href?: string | null;
}

const LIFETIME_MS = 6000;
const ERROR_LIFETIME_MS = 10000;
const MAX_TOASTS = 4;

const ToastContext = createContext<(toast: Toast) => void>(() => undefined);

export const useToast = () => useContext(ToastContext);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<(Toast & { id: number })[]>([]);
  const nextId = useRef(0);
  const timers = useRef(new Map<string, ReturnType<typeof setTimeout>>());
  // Hovered or focused toasts stay until the pointer and focus leave (WCAG 2.2.1).
  const paused = useRef(false);

  const dismiss = useCallback((key: string) => {
    clearTimeout(timers.current.get(key));
    timers.current.delete(key);
    setToasts((t) => t.filter((x) => x.key !== key));
  }, []);

  const schedule = useCallback((toast: Toast) => {
    clearTimeout(timers.current.get(toast.key));
    if (!paused.current) timers.current.set(toast.key, setTimeout(() => dismiss(toast.key), toast.tone === "red" ? ERROR_LIFETIME_MS : LIFETIME_MS));
  }, [dismiss]);

  const show = useCallback((toast: Toast) => {
    nextId.current += 1;
    const entry = { ...toast, id: nextId.current };
    setToasts((t) => [...t.filter((x) => x.key !== toast.key), entry].slice(-MAX_TOASTS));
    schedule(toast);
  }, [schedule]);

  const pause = () => {
    paused.current = true;
    timers.current.forEach(clearTimeout);
    timers.current.clear();
  };

  const resume = (list: HTMLElement) => {
    if (list.matches(":hover") || list.contains(document.activeElement)) return;
    paused.current = false;
    toasts.forEach(schedule);
  };

  // A toast removed under the pointer never fires mouseleave.
  useEffect(() => {
    if (toasts.length === 0) paused.current = false;
  }, [toasts.length]);

  useEffect(() => {
    const pending = timers.current;
    return () => pending.forEach(clearTimeout);
  }, []);

  return (
    <ToastContext value={show}>
      {children}
      <ol role="status" aria-live="polite" aria-label="Notifications"
        onMouseEnter={pause} onFocus={pause}
        onMouseLeave={(e) => resume(e.currentTarget)}
        onBlur={(e) => { if (!e.currentTarget.contains(e.relatedTarget)) resume(e.currentTarget); }}
        className="pointer-events-none fixed inset-x-4 bottom-20 z-[60] flex flex-col items-end gap-2 sm:inset-x-auto sm:right-5 sm:bottom-5 sm:w-[360px] lg:bottom-6">
        {toasts.map((t) => (
          <li key={t.id} className="pointer-events-auto flex w-full animate-toast items-start gap-3 rounded-xl border border-line bg-raised/95 p-3 pr-2 shadow-2xl shadow-black/60 backdrop-blur-xl">
            <span className={`grid size-8 shrink-0 place-items-center rounded-lg ring-1 ring-inset ${tones[t.tone]}`}><Icon name={t.icon} size={16} /></span>
            <div className="min-w-0 flex-1 py-0.5">
              {t.href
                ? <Link href={t.href} onClick={() => dismiss(t.key)} className="block truncate text-sm font-medium hover:text-accent">{t.title}</Link>
                : <p className="truncate text-sm font-medium">{t.title}</p>}
              {t.message && <p className="mt-0.5 line-clamp-2 text-xs leading-relaxed text-muted">{t.message}</p>}
            </div>
            <button onClick={() => dismiss(t.key)} aria-label="Dismiss" className="cursor-pointer rounded-md p-1 text-muted hover:bg-line hover:text-foreground">
              <Icon name="x" size={14} />
            </button>
          </li>
        ))}
      </ol>
    </ToastContext>
  );
}
