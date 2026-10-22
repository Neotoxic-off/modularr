"use client";

import Link from "next/link";
import { createContext, useContext, useEffect, useId, useRef, useState } from "react";
import type { ButtonHTMLAttributes, CSSProperties, InputHTMLAttributes, KeyboardEvent as ReactKeyboardEvent, ReactNode, RefObject, SelectHTMLAttributes } from "react";
import { createPortal } from "react-dom";
import { errorMessage } from "@/lib/api";
import { Icon } from "@/components/icons";
import type { IconName } from "@/components/icons";

const variants = {
  primary: "bg-white text-canvas hover:bg-white/85",
  glass: "glass text-ink hover:bg-fill-2",
  ghost: "text-ink-2 hover:bg-fill hover:text-ink",
  danger: "bg-bad/15 text-bad ring-1 ring-bad/30 hover:bg-bad/25",
} as const;

const sizes = { sm: "h-9 px-4 text-[13px] pointer-coarse:h-11", md: "h-11 px-5 text-sm" } as const;

// A spinner takes the icon's place while loading, so the label and the width stay put.
export function Button({ variant = "glass", size = "md", icon, loading = false, className = "", children, disabled, ...props }:
  ButtonHTMLAttributes<HTMLButtonElement> & { variant?: keyof typeof variants; size?: keyof typeof sizes; icon?: IconName; loading?: boolean }) {
  return (
    <button disabled={disabled || loading}
      className={`inline-flex shrink-0 cursor-pointer items-center justify-center gap-2 rounded-pill font-semibold whitespace-nowrap transition active:scale-[.98] disabled:pointer-events-none disabled:opacity-40 ${sizes[size]} ${variants[variant]} ${className}`}
      {...props}>
      {loading ? <Spinner /> : icon && <Icon name={icon} size={size === "sm" ? 15 : 17} />}
      {children}
    </button>
  );
}

export function IconButton({ icon, label, className = "", badge, ...props }:
  ButtonHTMLAttributes<HTMLButtonElement> & { icon: IconName; label: string; badge?: number }) {
  return (
    <button aria-label={label} title={label}
      className={`glass relative grid size-11 shrink-0 cursor-pointer place-items-center rounded-full text-ink-2 transition hover:bg-fill-2 hover:text-ink active:scale-[.98] disabled:opacity-40 ${className}`}
      {...props}>
      <Icon name={icon} size={19} />
      {!!badge && <Count value={badge} className="absolute -top-0.5 -right-0.5" />}
    </button>
  );
}

export function Count({ value, className = "" }: { value: number; className?: string }) {
  return (
    <span aria-hidden="true" className={`grid h-[18px] min-w-[18px] place-items-center rounded-pill bg-bad px-1 text-[10px] leading-none font-bold text-canvas tabular-nums ${className}`}>
      {value > 99 ? "99+" : value}
    </span>
  );
}

const control = "well w-full min-w-0 rounded-ctl text-sm text-ink outline-none transition placeholder:text-ink-3 hover:border-white/20 focus:border-white/40 focus-visible:outline-none";

export function Input({ className = "", ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={`h-11 px-3.5 ${control} ${className}`} {...props} />;
}

export function Select({ className = "", children, ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <div className={`relative ${className}`}>
      <select className={`h-11 cursor-pointer appearance-none pr-9 pl-3.5 [&>option]:bg-canvas ${control}`} {...props}>{children}</select>
      <Icon name="chevronDown" size={16} className="pointer-events-none absolute top-1/2 right-3 -translate-y-1/2 text-ink-3" />
    </div>
  );
}

export function Field({ label, hint, children }: { label: string; hint?: ReactNode; children: ReactNode }) {
  return (
    <label className="block space-y-2">
      <span className="text-sm font-medium">{label}</span>
      {children}
      {hint && <span className="block text-[13px] leading-relaxed text-ink-3">{hint}</span>}
    </label>
  );
}

// The whole row is the switch, so the hit area is the label as well as the track.
// hideLabel keeps the name for assistive tech only, for a switch inside a row that already says what it is.
export function Switch({ checked, onChange, label, description, disabled, hideLabel = false }: {
  checked: boolean;
  onChange: (checked: boolean) => void;
  label: string;
  description?: string;
  disabled?: boolean;
  hideLabel?: boolean;
}) {
  return (
    <button type="button" role="switch" aria-checked={checked} disabled={disabled} onClick={() => onChange(!checked)}
      className={`group flex min-h-11 cursor-pointer items-center gap-3 rounded-ctl text-left disabled:cursor-default disabled:opacity-40 ${hideLabel ? "min-w-11 shrink-0 justify-center" : "w-full"}`}>
      <span className={hideLabel ? "sr-only" : "min-w-0 flex-1"}>
        <span className="block text-sm font-medium">{label}</span>
        {description && <span className="block text-[13px] text-ink-3">{description}</span>}
      </span>
      <span className={`relative h-6 w-10 shrink-0 rounded-pill transition ${checked ? "bg-ok" : "bg-fill-2"}`}>
        <span className={`absolute top-0.5 left-0.5 size-5 rounded-full bg-white shadow transition-transform ${checked ? "translate-x-4" : ""}`} />
      </span>
    </button>
  );
}

export function Checkbox({ checked, onChange, label, description }: { checked: boolean; onChange: (checked: boolean) => void; label: string; description?: ReactNode }) {
  return (
    <label className="flex min-h-11 cursor-pointer items-start gap-3 py-1 text-sm">
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} className="mt-0.5 size-4 shrink-0 cursor-pointer accent-white" />
      <span className="min-w-0">
        <span className="block font-medium">{label}</span>
        {description && <span className="block text-[13px] break-words text-ink-3">{description}</span>}
      </span>
    </label>
  );
}

export function Chip({ selected = false, onClick, children, className = "" }: { selected?: boolean; onClick?: () => void; children: ReactNode; className?: string }) {
  return (
    <button type="button" onClick={onClick} aria-pressed={selected}
      className={`well inline-flex min-h-11 shrink-0 cursor-pointer items-center gap-1.5 rounded-pill px-4 text-[13px] font-medium text-ink-2 transition hover:text-ink aria-pressed:bg-fill-2 aria-pressed:text-ink sm:min-h-9 [@media(pointer:coarse)]:min-h-11 ${className}`}>
      {selected && <Icon name="check" size={14} strokeWidth={2.4} />}
      {children}
    </button>
  );
}

// An ordered pick of several values (first is most wanted): chips to remove, a select to add.
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
    <div role="group" aria-labelledby={id} className="space-y-2">
      <span id={id} className="block text-sm font-medium">{label}</span>
      <div className="flex flex-wrap items-center gap-2">
        {values.length === 0 && <span className="text-[13px] text-ink-3">{empty}</span>}
        {values.map((value) => (
          <span key={value} className="well inline-flex h-9 items-center gap-1 rounded-pill pr-1 pl-3.5 text-[13px] font-medium">
            {value}
            <button type="button" onClick={() => onChange(values.filter((v) => v !== value))} aria-label={`Remove ${value}`}
              className="grid size-7 cursor-pointer place-items-center rounded-full text-ink-3 hover:bg-fill-2 hover:text-ink">
              <Icon name="x" size={13} />
            </button>
          </span>
        ))}
        {left.length > 0 && (
          <span className="relative inline-flex h-9 items-center gap-1 rounded-pill border border-dashed border-hair px-3.5 text-[13px] font-medium text-ink-2 focus-within:outline-2 focus-within:outline-white hover:text-ink">
            <Icon name="plus" size={14} />Language
            <select value="" aria-label={`Add to ${label}`} onChange={(e) => e.target.value && onChange([...values, e.target.value])}
              className="absolute inset-0 cursor-pointer opacity-0 [&>option]:bg-canvas">
              <option value="">Add a language</option>
              {left.map((c) => <option key={c} value={c}>{c}</option>)}
            </select>
          </span>
        )}
      </div>
      {hint && <span className="block text-[13px] leading-relaxed text-ink-3">{hint}</span>}
    </div>
  );
}

export function Alert({ children, tone = "bad" }: { children: ReactNode; tone?: "bad" | "warn" }) {
  const colors = tone === "bad" ? "bg-bad/10 text-bad ring-bad/30" : "bg-warn/10 text-warn ring-warn/30";
  return (
    <div role="alert" className={`flex items-start gap-2.5 rounded-ctl px-4 py-3 text-sm ring-1 ${colors}`}>
      <Icon name="alert" size={16} className="mt-0.5 shrink-0" />
      <div className="min-w-0 break-words">{children}</div>
    </div>
  );
}

export type Tone = "neutral" | "ok" | "live" | "seek" | "subs" | "warn" | "bad";

const badgeTones: Record<Tone, string> = {
  neutral: "bg-fill text-ink-2 ring-hair",
  ok: "bg-ok/12 text-ok ring-ok/25",
  live: "bg-live/12 text-live ring-live/25",
  seek: "bg-seek/12 text-seek ring-seek/25",
  subs: "bg-subs/12 text-subs ring-subs/25",
  warn: "bg-warn/12 text-warn ring-warn/25",
  bad: "bg-bad/12 text-bad ring-bad/25",
};

export const toneText: Record<Tone, string> = {
  neutral: "text-ink-3", ok: "text-ok", live: "text-live", seek: "text-seek", subs: "text-subs", warn: "text-warn", bad: "text-bad",
};

export const toneDot: Record<Tone, string> = {
  neutral: "bg-ink-3", ok: "bg-ok", live: "bg-live", seek: "bg-seek", subs: "bg-subs", warn: "bg-warn", bad: "bg-bad",
};

export function Badge({ tone = "neutral", children, className = "" }: { tone?: Tone; children: ReactNode; className?: string }) {
  return <span className={`inline-flex h-6 items-center gap-1 rounded-pill px-2.5 text-[11px] font-semibold whitespace-nowrap ring-1 ring-inset ${badgeTones[tone]} ${className}`}>{children}</span>;
}

export type Status =
  | { state: "library" | "searching" | "missing" | "failed" | "unmonitored" }
  | { state: "downloading"; progress: number }
  | { state: "unreleased"; date: string | null };

const statusLook: Record<Status["state"], { tone: Tone; label: string }> = {
  library: { tone: "ok", label: "In library" },
  downloading: { tone: "live", label: "Downloading" },
  searching: { tone: "seek", label: "Searching" },
  missing: { tone: "warn", label: "Missing" },
  failed: { tone: "bad", label: "Failed" },
  unmonitored: { tone: "neutral", label: "Unmonitored" },
  unreleased: { tone: "neutral", label: "Not released" },
};

// Laid over artwork or on a tile; the text label always carries the state, the colour only repeats it.
export function StatusBadge({ status, className = "" }: { status: Status; className?: string }) {
  const { tone, label } = statusLook[status.state];
  const text = status.state === "downloading" ? `${label} ${Math.round(status.progress * 100)}%`
    : status.state === "unreleased" && status.date ? new Date(status.date).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" })
    : label;
  return (
    <span className={`inline-flex h-6 items-center gap-1.5 rounded-pill bg-black/55 px-2.5 text-[11px] font-semibold whitespace-nowrap ring-1 ring-white/10 backdrop-blur ${toneText[tone]} ${className}`}>
      {status.state === "library" ? <Icon name="check" size={12} strokeWidth={3} />
        : tone !== "neutral" && <span className={`size-1.5 rounded-full ${toneDot[tone]} ${status.state === "searching" ? "animate-pulse-dot" : ""}`} />}
      {text}
    </span>
  );
}

export function Rating({ value, className = "" }: { value: number | null; className?: string }) {
  if (value == null || value <= 0) return null;
  return (
    <span className={`inline-flex items-center gap-1 font-semibold text-gold tabular-nums ${className}`}>
      <Icon name="star" size={12} fill="currentColor" strokeWidth={0} />{value.toFixed(1)}
    </span>
  );
}

// Width moves smoothly, but assistive tech hears 10% steps only.
export function Progress({ value, eta, label = "Progress", tone = "ink", className = "" }: { value: number; eta?: string | null; label?: string; tone?: "ink" | "live"; className?: string }) {
  const step = Math.floor(Math.min(Math.max(value, 0), 1) * 10) * 10;
  return (
    <div role="progressbar" aria-label={label} aria-valuemin={0} aria-valuemax={100} aria-valuenow={step}
      aria-valuetext={`${step}%${eta ? `, ${eta} left` : ""}`} className={`h-1.5 overflow-hidden rounded-full bg-fill-2 ${className}`}>
      <div className={`h-full rounded-full transition-[width] duration-700 ${tone === "live" ? "bg-live" : "bg-ink"}`} style={{ width: `${value * 100}%` }} />
    </div>
  );
}

export function Section({ title, description, actions, children }: { title: string; description?: ReactNode; actions?: ReactNode; children: ReactNode }) {
  return (
    <section className="glass rounded-panel">
      <header className="flex flex-wrap items-start justify-between gap-3 border-b border-hair-2 px-5 py-4">
        <div className="min-w-0">
          <h2 className="text-[17px] font-semibold">{title}</h2>
          {description && <p className="mt-0.5 max-w-prose text-[13px] text-ink-3">{description}</p>}
        </div>
        {actions}
      </header>
      <div className="p-5">{children}</div>
    </section>
  );
}

export function PageHeader({ title, description, children }: { title: string; description?: ReactNode; children?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0">
        <h1 tabIndex={-1} className="font-display text-[40px] leading-none outline-none">{title}</h1>
        {description && <p className="mt-2 text-[13px] text-ink-3">{description}</p>}
      </div>
      {children}
    </div>
  );
}

export function Empty({ icon = "film", title, children }: { icon?: IconName; title: string; children?: ReactNode }) {
  return (
    <div className="glass flex flex-col items-center rounded-tile px-6 py-12 text-center">
      <span className="mb-4 grid size-12 place-items-center rounded-full bg-fill-2 text-ink-2"><Icon name={icon} size={22} /></span>
      <p className="text-[17px] font-semibold">{title}</p>
      {children && <div className="mt-1.5 max-w-sm text-[13px] text-ink-2">{children}</div>}
    </div>
  );
}

export function Spinner({ className = "" }: { className?: string }) {
  return <span role="status" aria-label="Loading" className={`inline-block size-4 animate-spin rounded-full border-2 border-current border-t-transparent ${className}`} />;
}

export function Skeleton({ className = "" }: { className?: string }) {
  return <div aria-hidden="true" className={`animate-shimmer rounded-chip bg-[linear-gradient(90deg,rgb(255_255_255/.05)_0%,rgb(255_255_255/.11)_50%,rgb(255_255_255/.05)_100%)] bg-[length:200%_100%] ${className}`} />;
}

export function Segmented<T extends string>({ value, options, onChange, label, className = "" }: {
  value: T;
  options: readonly { value: T; label: string; count?: number }[];
  onChange: (value: T) => void;
  label: string;
  className?: string;
}) {
  const move = (event: ReactKeyboardEvent, index: number) => {
    const step = event.key === "ArrowRight" || event.key === "ArrowDown" ? 1 : event.key === "ArrowLeft" || event.key === "ArrowUp" ? -1 : 0;
    if (!step) return;
    event.preventDefault();
    const target = (index + step + options.length) % options.length;
    const next = options[target];
    if (next) onChange(next.value);
    (event.currentTarget.parentElement?.children[target] as HTMLElement | undefined)?.focus();
  };
  return (
    <div role="radiogroup" aria-label={label} className={`glass rail inline-flex max-w-full gap-1 overflow-x-auto rounded-pill p-1 ${className}`}>
      {options.map((o, i) => (
        <button key={o.value} type="button" role="radio" aria-checked={o.value === value} tabIndex={o.value === value ? 0 : -1}
          onClick={() => onChange(o.value)} onKeyDown={(e) => move(e, i)}
          className="flex h-9 shrink-0 cursor-pointer items-center gap-1.5 rounded-pill px-4 text-[13px] font-semibold text-ink-2 transition hover:text-ink aria-checked:bg-fill-3 aria-checked:text-ink">
          {o.label}
          {o.count !== undefined && <span className="text-[11px] text-ink-3 tabular-nums">{o.count}</span>}
        </button>
      ))}
    </div>
  );
}

// The card every bento cell is made of. Loading, empty and error keep its height so the grid never jumps.
export function Tile({ title, link, className = "", style, children }: {
  title: string;
  link?: { href: string; label: string };
  className?: string;
  style?: CSSProperties;
  children: ReactNode;
}) {
  return (
    <section aria-label={title} style={style} className={`glass flex min-h-[180px] min-w-0 animate-rise flex-col gap-3 rounded-tile p-5 ${className}`}>
      <header className="flex items-center justify-between gap-3">
        <h2 className="caption truncate">{title}</h2>
        {link && <Link href={link.href} className="-my-2 -mr-2 shrink-0 rounded-pill px-2 py-2 text-[13px] font-medium whitespace-nowrap text-ink-2 hover:text-ink">{link.label} <span aria-hidden="true">→</span></Link>}
      </header>
      {children}
    </section>
  );
}

export function TileSkeleton() {
  return (
    <div className="flex flex-1 flex-col gap-2.5" aria-hidden="true">
      <Skeleton className="h-10 w-1/2" />
      <Skeleton className="h-4" />
      <Skeleton className="h-4 w-2/3" />
    </div>
  );
}

export function TileError({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div className="flex flex-1 flex-col items-start justify-center gap-3 text-[13px] text-ink-2">
      <p className="flex items-start gap-2"><span className="mt-1.5 size-2 shrink-0 rounded-full bg-bad" /><span><span className="sr-only">Error: </span>{message}</span></p>
      <Button size="sm" variant="ghost" icon="refresh" onClick={onRetry}>Retry</Button>
    </div>
  );
}

let openDialogs = 0;

// Focus trap, Escape, focus restore and an inert background: shared by every dialog, sheet and popover.
export function useDialog(panel: RefObject<HTMLElement | null>, onClose: () => void) {
  // Parents pass inline closures; the latest one is read so the setup runs once per opening.
  const close = useRef(onClose);
  useEffect(() => { close.current = onClose; });

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    (panel.current?.querySelector<HTMLElement>("[autofocus], input, select") ?? panel.current)?.focus();
    const key = (e: KeyboardEvent) => {
      if (e.key === "Escape") { e.stopPropagation(); close.current(); }
      if (e.key !== "Tab" || !panel.current) return;
      const focusable = [...panel.current.querySelectorAll<HTMLElement>("button, input, select, a[href], [tabindex='0']")].filter((el) => !el.hasAttribute("disabled"));
      const [first, last] = [focusable[0], focusable[focusable.length - 1]];
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last?.focus(); }
      else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first?.focus(); }
    };
    const app = document.getElementById("app");
    openDialogs += 1;
    app?.setAttribute("inert", "");
    document.body.style.overflow = "hidden";
    window.addEventListener("keydown", key);
    return () => {
      window.removeEventListener("keydown", key);
      openDialogs -= 1;
      if (openDialogs === 0) {
        app?.removeAttribute("inert");
        document.body.style.overflow = "";
      }
      previous?.focus();
    };
  }, [panel]);
}

// Below 640px every dialog is a bottom sheet; "popover" stays a sheet up to 1024px, then hangs under the header.
const modalLayouts = {
  md: { overlay: "sm:items-center sm:p-6", panel: "sm:max-w-xl sm:rounded-panel" },
  lg: { overlay: "sm:items-center sm:p-6", panel: "sm:max-w-4xl sm:rounded-panel" },
  popover: { overlay: "lg:items-start lg:justify-end lg:bg-transparent lg:pt-[84px] lg:pr-5", panel: "sm:max-w-xl lg:max-h-[min(680px,82dvh)] lg:w-[380px] lg:rounded-tile" },
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
  useDialog(panel, onClose);

  return createPortal(
    <div className={`fixed inset-0 z-50 flex items-end justify-center bg-black/60 ${modalLayouts[size].overlay}`} onMouseDown={onClose}>
      <div ref={panel} role="dialog" aria-modal="true" aria-labelledby={id} tabIndex={-1} onMouseDown={(e) => e.stopPropagation()}
        className={`glass-pop relative flex max-h-[92dvh] w-full animate-rise flex-col overflow-hidden rounded-t-[28px] outline-none ${modalLayouts[size].panel}`}>
        <span aria-hidden="true" className={`mx-auto mt-2 h-1 w-10 shrink-0 rounded-full bg-fill-3 sm:hidden ${size === "popover" ? "lg:hidden" : ""}`} />
        {banner && (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={banner} alt="" className="pointer-events-none absolute inset-x-0 top-0 h-48 w-full object-cover opacity-35 [mask-image:linear-gradient(to_bottom,black,transparent)]" />
        )}
        <header className="relative flex items-start justify-between gap-4 px-5 pt-4 pb-3">
          <div className="min-w-0 pt-2">
            <h2 id={id} className="text-[17px] font-semibold">{title}</h2>
            {description && <div className="mt-0.5 text-[13px] text-ink-2">{description}</div>}
          </div>
          <div className="-mr-2 flex shrink-0 items-center gap-1">
            {actions}
            <button onClick={onClose} aria-label="Close" className="grid size-11 cursor-pointer place-items-center rounded-full text-ink-2 hover:bg-fill-2 hover:text-ink">
              <Icon name="x" />
            </button>
          </div>
        </header>
        <div className="relative min-h-0 flex-1 overflow-y-auto">{children}</div>
        {footer && <footer className="relative flex flex-wrap justify-end gap-2 border-t border-hair-2 px-5 pt-3.5 pb-[max(14px,env(safe-area-inset-bottom))]">{footer}</footer>}
      </div>
    </div>,
    document.body,
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
        <Button variant="primary" loading={busy} disabled={disabled}>{label}</Button>
        {done && !busy && <span className="flex animate-fade items-center gap-1 text-[13px] text-ok"><Icon name="check" size={16} />Saved</span>}
      </div>
    </div>
  );
}

const CloseMenu = createContext<() => void>(() => undefined);

// A dropdown of actions: arrow keys move, Escape and outside clicks close it and focus goes back to the trigger.
export function Menu({ label, trigger, className = "", align = "right", children }: {
  label: string;
  trigger?: ReactNode;
  className?: string;
  align?: "left" | "right";
  children: ReactNode;
}) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const items = () => [...(root.current?.querySelectorAll<HTMLElement>("[role=menuitem]:not(:disabled)") ?? [])];
    const outside = (e: MouseEvent) => { if (!root.current?.contains(e.target as Node)) setOpen(false); };
    const key = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.stopPropagation();
        setOpen(false);
        root.current?.querySelector("button")?.focus();
      }
      if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
      e.preventDefault();
      const list = items();
      const at = list.indexOf(document.activeElement as HTMLElement);
      list[(at + (e.key === "ArrowDown" ? 1 : -1) + list.length) % list.length]?.focus();
    };
    document.addEventListener("mousedown", outside);
    document.addEventListener("keydown", key);
    items()[0]?.focus();
    return () => {
      document.removeEventListener("mousedown", outside);
      document.removeEventListener("keydown", key);
    };
  }, [open]);

  return (
    <div ref={root} className="relative">
      <button type="button" onClick={() => setOpen(!open)} aria-haspopup="menu" aria-expanded={open} aria-label={label} title={label}
        className={className || "glass grid size-11 cursor-pointer place-items-center rounded-full text-ink-2 transition hover:bg-fill-2 hover:text-ink"}>
        {trigger ?? <Icon name="more" size={20} strokeWidth={3} />}
      </button>
      {open && (
        // Nearly opaque: a menu often opens over another blurred panel, whose own blur cannot reach behind it.
        <div role="menu" aria-label={label} className={`glass-pop absolute top-full z-40 mt-2 w-64 animate-rise rounded-tile bg-[rgb(10_10_10/.97)] p-2 ${align === "right" ? "right-0" : "left-0"}`}>
          <CloseMenu value={() => setOpen(false)}>{children}</CloseMenu>
        </div>
      )}
    </div>
  );
}

export function MenuItem({ icon, href, onClick, danger = false, disabled, className = "", children }: {
  icon: IconName;
  href?: string;
  onClick?: () => void;
  danger?: boolean;
  disabled?: boolean;
  className?: string;
  children: ReactNode;
}) {
  const close = useContext(CloseMenu);
  const look = `flex min-h-11 w-full cursor-pointer items-center gap-3 rounded-ctl px-3 text-left text-sm font-medium transition hover:bg-fill-2 disabled:opacity-40 ${danger ? "text-bad" : "text-ink-2 hover:text-ink"} ${className}`;
  const content = <><Icon name={icon} size={17} className="shrink-0" />{children}</>;
  return href
    ? <Link role="menuitem" href={href} onClick={close} className={look}>{content}</Link>
    : <button role="menuitem" type="button" disabled={disabled} onClick={() => { close(); onClick?.(); }} className={look}>{content}</button>;
}

export function Logo({ markOnly = false }: { markOnly?: boolean }) {
  return (
    <span className="flex items-center gap-2.5">
      <span className="grid size-8 shrink-0 place-items-center rounded-[11px] bg-white font-display text-[22px] leading-none text-canvas">m</span>
      {markOnly ? <span className="sr-only">modularr</span> : <span className="font-display text-[24px] leading-none">modularr</span>}
    </span>
  );
}
