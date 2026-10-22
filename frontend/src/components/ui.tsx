"use client";

import { useEffect, useId, useRef, useState } from "react";
import type { ButtonHTMLAttributes, InputHTMLAttributes, KeyboardEvent as ReactKeyboardEvent, ReactNode, SelectHTMLAttributes } from "react";
import { errorMessage } from "@/lib/api";
import { Icon } from "@/components/icons";

const variants = {
  primary: "bg-accent-strong text-white shadow-[0_1px_0_rgba(255,255,255,0.15)_inset,0_8px_24px_-10px_var(--color-accent-strong)] hover:bg-accent",
  secondary: "bg-raised text-foreground ring-1 ring-line hover:bg-line",
  ghost: "text-muted hover:bg-raised hover:text-foreground",
  danger: "bg-red-500/15 text-red-200 ring-1 ring-red-500/25 hover:bg-red-500/25",
  glass: "bg-white/10 text-white ring-1 ring-white/15 backdrop-blur-md hover:bg-white/20",
} as const;

const sizes = { sm: "h-8 px-2.5 text-xs", md: "h-9 px-3.5 text-sm", lg: "h-11 px-5 text-sm" } as const;

export function Button({ variant = "secondary", size = "md", className = "", ...props }:
  ButtonHTMLAttributes<HTMLButtonElement> & { variant?: keyof typeof variants; size?: keyof typeof sizes }) {
  return (
    <button
      className={`inline-flex shrink-0 cursor-pointer items-center justify-center gap-2 rounded-lg font-medium whitespace-nowrap transition active:scale-[0.98] disabled:pointer-events-none disabled:opacity-50 ${sizes[size]} ${variants[variant]} ${className}`}
      {...props}
    />
  );
}

const control = "w-full min-w-0 rounded-lg border border-line bg-background/60 text-sm outline-none transition placeholder:text-muted/60 hover:border-muted/40 focus:border-accent focus:ring-2 focus:ring-accent/20 focus-visible:outline-none";

export function Input({ className = "", ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={`h-9 px-3 ${control} ${className}`} {...props} />;
}

export function Select({ className = "", children, ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <div className={`relative ${className}`}>
      <select className={`h-9 cursor-pointer appearance-none pr-8 pl-3 ${control}`} {...props}>{children}</select>
      <Icon name="chevronDown" size={16} className="pointer-events-none absolute top-1/2 right-2.5 -translate-y-1/2 text-muted" />
    </div>
  );
}

export function Field({ label, hint, children }: { label: string; hint?: ReactNode; children: ReactNode }) {
  return (
    <label className="block space-y-1.5">
      <span className="text-sm font-medium">{label}</span>
      {children}
      {hint && <span className="block text-xs leading-relaxed text-muted">{hint}</span>}
    </label>
  );
}

export function Switch({ checked, onChange, label, description, disabled }: {
  checked: boolean;
  onChange: (checked: boolean) => void;
  label: string;
  description?: string;
  disabled?: boolean;
}) {
  return (
    <label className={`flex items-start gap-3 ${disabled ? "opacity-50" : "cursor-pointer"}`}>
      <button type="button" role="switch" aria-checked={checked} disabled={disabled} onClick={() => onChange(!checked)}
        className={`relative mt-0.5 h-5 w-9 shrink-0 cursor-pointer rounded-full transition ${checked ? "bg-accent-strong" : "bg-line"}`}>
        <span className={`absolute top-0.5 left-0.5 size-4 rounded-full bg-white shadow transition-transform ${checked ? "translate-x-4" : ""}`} />
      </button>
      <span className="min-w-0">
        <span className="block text-sm">{label}</span>
        {description && <span className="block text-xs text-muted">{description}</span>}
      </span>
    </label>
  );
}

// An ordered pick of several values (first is most wanted): chips to remove, a select to append.
export function PickList({ label, hint, values, choices, onChange, empty = "None" }: {
  label: string;
  hint?: ReactNode;
  values: readonly string[];
  choices: readonly string[];
  onChange: (values: string[]) => void;
  empty?: string;
}) {
  const id = useId();
  const left = choices.filter((c) => !values.includes(c));
  return (
    <div role="group" aria-labelledby={id} className="space-y-1.5">
      <span id={id} className="block text-sm font-medium">{label}</span>
      <div className="flex flex-wrap items-center gap-1.5">
        {values.length === 0 && <span className="text-sm text-muted">{empty}</span>}
        {values.map((value, i) => (
          <span key={value} className="inline-flex items-center gap-1 rounded-md bg-raised py-0.5 pr-0.5 pl-2 text-xs ring-1 ring-line">
            <span className="text-muted tabular-nums">{i + 1}</span>{value}
            <button type="button" onClick={() => onChange(values.filter((v) => v !== value))} aria-label={`Remove ${value}`}
              className="cursor-pointer rounded p-0.5 text-muted hover:bg-line hover:text-foreground">
              <Icon name="x" size={12} />
            </button>
          </span>
        ))}
      </div>
      {left.length > 0 && (
        <Select value="" aria-label={`Add to ${label}`} onChange={(e) => e.target.value && onChange([...values, e.target.value])}>
          <option value="">Add…</option>
          {left.map((c) => <option key={c} value={c}>{c}</option>)}
        </Select>
      )}
      {hint && <span className="block text-xs leading-relaxed text-muted">{hint}</span>}
    </div>
  );
}

export function Alert({ children, tone = "red" }: { children: ReactNode; tone?: "red" | "amber" }) {
  const colors = tone === "red" ? "border-red-500/25 bg-red-500/10 text-red-200" : "border-amber-400/25 bg-amber-400/10 text-amber-100";
  return (
    <div role="alert" className={`flex items-start gap-2.5 rounded-lg border px-3.5 py-2.5 text-sm ${colors}`}>
      <Icon name="alert" size={16} className="mt-0.5 shrink-0" />
      <div className="min-w-0 break-words">{children}</div>
    </div>
  );
}

export const tones = {
  neutral: "bg-white/5 text-muted ring-white/10",
  accent: "bg-accent/15 text-accent ring-accent/25",
  green: "bg-emerald-400/10 text-emerald-300 ring-emerald-400/20",
  amber: "bg-amber-400/10 text-amber-200 ring-amber-400/20",
  red: "bg-red-500/10 text-red-300 ring-red-500/20",
} as const;

export type Tone = keyof typeof tones;

export function Badge({ tone = "neutral", children, className = "" }: { tone?: Tone; children: ReactNode; className?: string }) {
  return <span className={`inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 text-[11px] font-medium ring-1 ring-inset ${tones[tone]} ${className}`}>{children}</span>;
}

export function Rating({ value, className = "" }: { value: number | null; className?: string }) {
  if (value == null || value <= 0) return null;
  return (
    <span className={`inline-flex items-center gap-1 text-xs font-semibold text-gold tabular-nums ${className}`}>
      <Icon name="star" size={12} fill="currentColor" strokeWidth={0} />{value.toFixed(1)}
    </span>
  );
}

export function Section({ title, description, actions, children }: { title: string; description?: ReactNode; actions?: ReactNode; children: ReactNode }) {
  return (
    <section className="rounded-2xl border border-line bg-surface">
      <header className="flex flex-wrap items-start justify-between gap-3 border-b border-line px-5 py-4">
        <div className="min-w-0">
          <h2 className="font-display text-base font-semibold">{title}</h2>
          {description && <p className="mt-0.5 max-w-prose text-sm text-muted">{description}</p>}
        </div>
        {actions}
      </header>
      <div className="p-5">{children}</div>
    </section>
  );
}

export function PageHeader({ title, description, children }: { title: string; description?: ReactNode; children?: ReactNode }) {
  return (
    <div className="mb-8 flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0">
        <h1 className="font-display text-3xl font-semibold tracking-tight sm:text-4xl">{title}</h1>
        {description && <p className="mt-1.5 text-sm text-muted">{description}</p>}
      </div>
      {children}
    </div>
  );
}

export function Empty({ icon = "film", title, children }: { icon?: Parameters<typeof Icon>[0]["name"]; title: string; children?: ReactNode }) {
  return (
    <div className="flex flex-col items-center rounded-2xl border border-dashed border-line px-6 py-14 text-center">
      <span className="mb-4 flex size-12 items-center justify-center rounded-full bg-raised text-muted"><Icon name={icon} size={22} /></span>
      <p className="font-medium">{title}</p>
      {children && <div className="mt-1.5 max-w-sm text-sm text-muted">{children}</div>}
    </div>
  );
}

export function Spinner({ className = "" }: { className?: string }) {
  return <span role="status" aria-label="Loading" className={`inline-block size-4 animate-spin rounded-full border-2 border-current border-t-transparent ${className}`} />;
}

export function Skeleton({ className = "" }: { className?: string }) {
  return <div aria-hidden="true" className={`animate-shimmer rounded-lg bg-[linear-gradient(90deg,var(--color-surface)_0%,var(--color-raised)_50%,var(--color-surface)_100%)] bg-[length:200%_100%] ${className}`} />;
}

export function Tabs<T extends string>({ value, options, onChange, label }: {
  value: T;
  options: readonly { value: T; label: string; count?: number }[];
  onChange: (value: T) => void;
  label: string;
}) {
  const move = (event: ReactKeyboardEvent, index: number) => {
    const step = event.key === "ArrowRight" ? 1 : event.key === "ArrowLeft" ? -1 : 0;
    if (!step) return;
    const target = (index + step + options.length) % options.length;
    const next = options[target];
    if (next) onChange(next.value);
    (event.currentTarget.parentElement?.children[target] as HTMLElement | undefined)?.focus();
  };
  return (
    <div role="tablist" aria-label={label} className="rail inline-flex max-w-full gap-1 overflow-x-auto rounded-xl border border-line bg-surface p-1">
      {options.map((o, i) => (
        <button key={o.value} role="tab" aria-selected={o.value === value} tabIndex={o.value === value ? 0 : -1}
          onClick={() => onChange(o.value)} onKeyDown={(e) => move(e, i)}
          className="flex shrink-0 cursor-pointer items-center gap-1.5 rounded-lg px-3 py-1.5 text-sm text-muted transition hover:text-foreground aria-selected:bg-raised aria-selected:text-foreground aria-selected:shadow-sm">
          {o.label}
          {o.count !== undefined && <span className="rounded-full bg-white/5 px-1.5 text-[11px] text-muted tabular-nums">{o.count}</span>}
        </button>
      ))}
    </div>
  );
}

const dialog = "max-h-[92vh] animate-rise rounded-t-2xl border sm:rounded-2xl";

// "side" is a full-height sheet from the right edge, for panels that list rather than ask.
const modalLayouts = {
  md: { overlay: "items-end justify-center sm:items-center sm:p-6", panel: `max-w-xl ${dialog}` },
  lg: { overlay: "items-end justify-center sm:items-center sm:p-6", panel: `max-w-4xl ${dialog}` },
  side: { overlay: "justify-end", panel: "h-full max-w-md animate-slide border-l" },
} as const;

export function Modal({ title, description, actions, banner, onClose, size = "md", children, footer }: {
  title: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  banner?: string | null;
  onClose: () => void;
  size?: keyof typeof modalLayouts;
  children: ReactNode;
  footer?: ReactNode;
}) {
  const id = useId();
  const panel = useRef<HTMLDivElement>(null);
  // Parents pass inline closures; the latest one is read so focus setup runs once per opening.
  const close = useRef(onClose);
  useEffect(() => { close.current = onClose; });

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    (panel.current?.querySelector<HTMLElement>("[autofocus], input, select") ?? panel.current)?.focus();
    const key = (e: KeyboardEvent) => {
      if (e.key === "Escape") close.current();
      if (e.key !== "Tab" || !panel.current) return;
      // Keeps keyboard focus inside the dialog.
      const focusable = [...panel.current.querySelectorAll<HTMLElement>("button, input, select, a[href], [tabindex='0']")].filter((el) => !el.hasAttribute("disabled"));
      const [first, last] = [focusable[0], focusable[focusable.length - 1]];
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last?.focus(); }
      else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first?.focus(); }
    };
    window.addEventListener("keydown", key);
    document.body.style.overflow = "hidden";
    return () => {
      window.removeEventListener("keydown", key);
      document.body.style.overflow = "";
      previous?.focus();
    };
  }, []);

  return (
    <div className={`fixed inset-0 z-50 flex animate-fade bg-black/70 backdrop-blur-sm ${modalLayouts[size].overlay}`} onMouseDown={onClose}>
      <div ref={panel} role="dialog" aria-modal="true" aria-labelledby={id} tabIndex={-1} onMouseDown={(e) => e.stopPropagation()}
        className={`relative flex w-full flex-col overflow-hidden border-line bg-surface shadow-2xl shadow-black/60 outline-none ${modalLayouts[size].panel}`}>
        {banner && (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={banner} alt="" className="pointer-events-none absolute inset-x-0 top-0 h-44 w-full object-cover opacity-25 [mask-image:linear-gradient(to_bottom,black,transparent)]" />
        )}
        <header className="relative flex items-start justify-between gap-4 px-5 pt-5 pb-3">
          <div className="min-w-0">
            <h2 id={id} className="font-display text-lg font-semibold">{title}</h2>
            {description && <div className="mt-0.5 text-sm text-muted">{description}</div>}
          </div>
          <div className="flex shrink-0 items-center gap-1">
            {actions}
            <button onClick={onClose} aria-label="Close" className="-mr-1 cursor-pointer rounded-lg p-1.5 text-muted hover:bg-raised hover:text-foreground">
              <Icon name="x" />
            </button>
          </div>
        </header>
        <div className="relative min-h-0 flex-1 overflow-y-auto">{children}</div>
        {footer && <footer className="relative flex flex-wrap justify-end gap-2 border-t border-line bg-background/40 px-5 py-3.5">{footer}</footer>}
      </div>
    </div>
  );
}

// Runs a save or delete so every form reports errors and success the same way.
export function useAction() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    setDone(false);
    try {
      await action();
      setDone(true);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };
  return { busy, error, done, run, setError };
}

export function SaveBar({ busy, error, done, label = "Save changes", disabled }: { busy: boolean; error: string | null; done: boolean; label?: string; disabled?: boolean }) {
  return (
    <div className="space-y-3 pt-2">
      {error && <Alert>{error}</Alert>}
      <div className="flex items-center gap-3">
        <Button variant="primary" disabled={busy || disabled}>{busy ? <Spinner /> : label}</Button>
        {done && !busy && <span className="flex animate-fade items-center gap-1 text-sm text-emerald-300"><Icon name="check" size={16} />Saved</span>}
      </div>
    </div>
  );
}

export function Logo({ markOnly = false }: { markOnly?: boolean }) {
  return (
    <span className="flex items-center gap-2 font-display text-lg font-semibold tracking-tight">
      <span className="grid size-7 place-items-center rounded-lg bg-gradient-to-br from-accent to-accent-strong text-white shadow-lg shadow-accent-strong/30">
        <Icon name="play" size={12} fill="currentColor" strokeWidth={0} />
      </span>
      {markOnly ? <span className="sr-only">modularr</span> : "modularr"}
    </span>
  );
}
