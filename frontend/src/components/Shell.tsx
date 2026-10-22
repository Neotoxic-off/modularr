"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { useSession } from "@/components/AuthGate";
import { ActivityProvider, useActivity } from "@/components/Activity";
import { NotificationPanel } from "@/components/ActivityFeed";
import { AddDialogProvider } from "@/components/AddDialog";
import { Icon } from "@/components/icons";
import type { IconName } from "@/components/icons";
import { SearchPalette } from "@/components/SearchPalette";
import { ToastProvider } from "@/components/Toasts";
import { Logo } from "@/components/ui";

type Badge = "downloads" | "unread";
type NavItem = { label: string; icon: IconName; badge?: Badge; admin?: boolean } & ({ href: string } | { action: "notifications" });

const sections: readonly { label: string | null; items: readonly NavItem[] }[] = [
  { label: null, items: [{ href: "/", label: "Discover", icon: "compass" }] },
  { label: "Library", items: [
    { href: "/movies/", label: "Movies", icon: "film" },
    { href: "/series/", label: "Series", icon: "tv" },
    { href: "/anime/", label: "Anime", icon: "sparkles" },
  ] },
  { label: "Activity", items: [
    { href: "/downloads/", label: "Downloads", icon: "download", badge: "downloads" },
    { action: "notifications", label: "Notifications", icon: "bell", badge: "unread" },
  ] },
  { label: "System", items: [
    { href: "/import/", label: "Library import", icon: "folder", admin: true },
    { href: "/settings/", label: "Settings", icon: "settings" },
  ] },
];

// System pages move to the account menu in the header on small screens.
const mobileLinks = sections.filter((s) => s.label !== "System").flatMap((s) => s.items).filter((i): i is NavItem & { href: string } => "href" in i);

const COLLAPSED_KEY = "modularr.sidebar.collapsed";

export function Shell({ children }: { children: ReactNode }) {
  return (
    <ToastProvider>
      <ActivityProvider>
        <AddDialogProvider>
          <Frame>{children}</Frame>
        </AddDialogProvider>
      </ActivityProvider>
    </ToastProvider>
  );
}

function Frame({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const { user } = useSession();
  const { unread, activeDownloads } = useActivity();
  const [searching, setSearching] = useState(false);
  const [notifying, setNotifying] = useState(false);
  // The shell only renders in the browser, after the auth gate, so storage can be read up front.
  const [collapsed, setCollapsed] = useState(() => {
    try { return localStorage.getItem(COLLAPSED_KEY) === "1"; } catch { return false; }
  });

  const toggleCollapsed = () => {
    setCollapsed(!collapsed);
    try { localStorage.setItem(COLLAPSED_KEY, collapsed ? "0" : "1"); } catch { /* not remembered */ }
  };

  useEffect(() => {
    const key = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setSearching((s) => !s);
      }
    };
    window.addEventListener("keydown", key);
    return () => window.removeEventListener("keydown", key);
  }, []);

  const counts: Record<Badge, number> = { downloads: activeDownloads, unread };
  const current = (href: string) => ((href === "/" ? pathname === "/" : pathname.startsWith(href)) ? "page" : undefined);

  return (
    <div className="flex min-h-screen">
      <aside data-collapsed={collapsed || undefined}
        className="group/side sticky top-0 hidden h-screen w-64 shrink-0 flex-col border-r border-line/70 bg-surface/60 transition-[width] duration-200 data-collapsed:w-[72px] lg:flex">
        <div className="flex h-16 shrink-0 items-center justify-between gap-2 px-4 group-data-collapsed/side:h-auto group-data-collapsed/side:flex-col group-data-collapsed/side:justify-center group-data-collapsed/side:px-0 group-data-collapsed/side:pt-4 group-data-collapsed/side:pb-3">
          <Link href="/" aria-label="Modularr home" className="rounded-lg"><Logo markOnly={collapsed} /></Link>
          <button onClick={toggleCollapsed} aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"} aria-expanded={!collapsed}
            title={collapsed ? "Expand sidebar" : "Collapse sidebar"}
            className="grid size-8 cursor-pointer place-items-center rounded-lg text-muted transition hover:bg-raised hover:text-foreground">
            <Icon name="panel" size={17} />
          </button>
        </div>

        <div className="px-3 pb-2">
          <SearchTrigger collapsed={collapsed} onOpen={() => setSearching(true)} />
        </div>

        <nav aria-label="Main" className="flex flex-1 flex-col gap-5 overflow-y-auto px-3 py-3">
          {sections.map((section) => (
            <div key={section.label ?? "home"} className="flex flex-col gap-0.5">
              {section.label && (
                <>
                  <p className="px-3 pb-1.5 text-[10.5px] font-semibold tracking-[0.09em] text-muted/70 uppercase group-data-collapsed/side:hidden">{section.label}</p>
                  <span aria-hidden="true" className="mx-3 mb-2 hidden h-px bg-line group-data-collapsed/side:block" />
                </>
              )}
              {section.items.filter((item) => !item.admin || user.role === "Admin").map((item) => (
                <SideItem key={item.label} item={item} count={item.badge && counts[item.badge]}
                  current={"href" in item ? current(item.href) : undefined} onAction={() => setNotifying(true)} />
              ))}
            </div>
          ))}
        </nav>

        <div className="border-t border-line/70 p-3">
          <UserMenu placement="up" compact={collapsed} />
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-30 flex h-14 items-center gap-1 border-b border-line/70 bg-background/80 px-4 backdrop-blur-xl lg:hidden">
          <Link href="/" className="mr-auto rounded-lg"><Logo /></Link>
          <button onClick={() => setSearching(true)} aria-label="Search" className={headerButton}>
            <Icon name="search" size={20} />
          </button>
          <button onClick={() => setNotifying(true)} aria-label={unread ? `Notifications, ${unread} unread` : "Notifications"} className={`relative ${headerButton}`}>
            <Icon name="bell" size={20} />
            {unread > 0 && <Count value={unread} className="absolute top-1 right-1" />}
          </button>
          <UserMenu placement="down" compact />
        </header>
        <main key={pathname} className="w-full max-w-[1600px] min-w-0 flex-1 animate-fade px-4 pt-6 pb-28 sm:px-6 lg:px-10 lg:pt-8 lg:pb-12">{children}</main>
      </div>

      <nav aria-label="Main" className="fixed inset-x-0 bottom-0 z-30 grid grid-cols-5 border-t border-line/70 bg-background/90 px-2 pt-1.5 pb-[max(0.375rem,env(safe-area-inset-bottom))] backdrop-blur-xl lg:hidden">
        {mobileLinks.map(({ href, label, icon, badge }) => (
          <Link key={href} href={href} aria-current={current(href)}
            className="group relative flex flex-col items-center gap-1 rounded-lg py-1.5 text-[11px] font-medium text-muted transition aria-[current=page]:text-foreground">
            <span className="relative grid h-7 w-12 place-items-center rounded-full transition group-aria-[current=page]:bg-accent/15 group-aria-[current=page]:text-accent">
              <Icon name={icon} size={19} />
              {badge && counts[badge] > 0 && <Count value={counts[badge]} className="absolute -top-1 right-0.5" />}
            </span>
            {label}
          </Link>
        ))}
      </nav>

      {searching && <SearchPalette onClose={() => setSearching(false)} />}
      {notifying && <NotificationPanel onClose={() => setNotifying(false)} />}
    </div>
  );
}

const headerButton = "grid size-10 cursor-pointer place-items-center rounded-lg text-muted transition hover:bg-raised hover:text-foreground";

function Count({ value, className = "" }: { value: number; className?: string }) {
  return (
    <span className={`grid h-4 min-w-4 place-items-center rounded-full bg-accent-strong px-1 text-[10px] leading-none font-semibold text-white tabular-nums ring-2 ring-background ${className}`}>
      {value > 99 ? "99+" : value}
    </span>
  );
}

function SearchTrigger({ collapsed, onOpen }: { collapsed: boolean; onOpen: () => void }) {
  const shortcut = /Mac|iPhone|iPad/.test(navigator.platform) ? "⌘K" : "Ctrl K";
  return (
    <button onClick={onOpen} aria-label="Search" aria-keyshortcuts="Control+K Meta+K" title={collapsed ? `Search (${shortcut})` : undefined}
      className={`flex h-9 w-full cursor-pointer items-center gap-2.5 rounded-lg border border-line bg-background/60 text-sm text-muted transition hover:border-muted/40 hover:text-foreground ${collapsed ? "justify-center" : "px-3"}`}>
      <Icon name="search" size={16} className="shrink-0" />
      {!collapsed && (
        <>
          <span className="flex-1 text-left">Search titles</span>
          <kbd className="rounded border border-line px-1.5 py-px font-sans text-[10px] text-muted">{shortcut}</kbd>
        </>
      )}
    </button>
  );
}

function SideItem({ item, count, current, onAction }: { item: NavItem; count?: number; current?: "page"; onAction: () => void }) {
  const content = (
    <>
      <span aria-hidden="true" className="absolute top-1.5 bottom-1.5 -left-3 w-[3px] rounded-r-full bg-accent opacity-0 transition group-aria-[current=page]:opacity-100" />
      <span className="relative shrink-0 text-muted transition group-hover:text-foreground group-aria-[current=page]:text-accent">
        <Icon name={item.icon} size={18} />
        {!!count && <Count value={count} className="absolute -top-1.5 -right-2 hidden group-data-collapsed/side:grid" />}
      </span>
      <span className="flex-1 truncate group-data-collapsed/side:sr-only">{item.label}</span>
      {!!count && (
        <span className="rounded-md bg-accent/15 px-1.5 py-0.5 text-[11px] leading-none font-semibold text-accent tabular-nums group-data-collapsed/side:hidden">
          {count > 99 ? "99+" : count}
        </span>
      )}
    </>
  );
  const className = "group relative flex h-9 w-full cursor-pointer items-center gap-3 rounded-lg px-3 text-left text-sm text-foreground/75 transition hover:bg-raised/70 hover:text-foreground aria-[current=page]:bg-raised aria-[current=page]:font-medium aria-[current=page]:text-foreground group-data-collapsed/side:justify-center group-data-collapsed/side:px-0";
  return "href" in item
    ? <Link href={item.href} aria-current={current} title={item.label} className={className}>{content}</Link>
    : <button onClick={onAction} title={item.label} className={className}>{content}</button>;
}

function UserMenu({ placement, compact }: { placement: "up" | "down"; compact: boolean }) {
  const { user, logout } = useSession();
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const role = user.role === "Admin" ? "Administrator" : "Member";

  useEffect(() => {
    if (!open) return;
    const outside = (e: MouseEvent) => { if (!root.current?.contains(e.target as Node)) setOpen(false); };
    const escape = (e: KeyboardEvent) => { if (e.key === "Escape") setOpen(false); };
    document.addEventListener("mousedown", outside);
    document.addEventListener("keydown", escape);
    return () => {
      document.removeEventListener("mousedown", outside);
      document.removeEventListener("keydown", escape);
    };
  }, [open]);

  const item = "flex w-full cursor-pointer items-center gap-2.5 rounded-md px-2.5 py-2 text-sm text-foreground/85 hover:bg-line/60 hover:text-foreground";

  return (
    <div ref={root} className="relative">
      <button onClick={() => setOpen(!open)} aria-haspopup="menu" aria-expanded={open} aria-label={`Account menu for ${user.username}`}
        className={`flex w-full cursor-pointer items-center gap-3 rounded-lg text-left transition hover:bg-raised/70 ${compact ? "justify-center p-1" : "p-2"}`}>
        <span className="grid size-8 shrink-0 place-items-center rounded-full bg-accent/15 text-sm font-semibold text-accent uppercase ring-1 ring-accent/25">{user.username[0]}</span>
        {!compact && (
          <>
            <span className="min-w-0 flex-1">
              <span className="block truncate text-sm font-medium">{user.username}</span>
              <span className="block text-xs text-muted">{role}</span>
            </span>
            <Icon name="upDown" size={14} className="text-muted" />
          </>
        )}
      </button>
      {open && (
        <div role="menu" aria-label="Account"
          className={`absolute z-50 w-56 animate-rise rounded-xl border border-line bg-raised p-1.5 shadow-2xl shadow-black/60 ${placement === "up" ? "bottom-full left-0 mb-2" : "top-full right-0 mt-2"}`}>
          <div className="mb-1 border-b border-line px-2.5 pt-1.5 pb-2.5">
            <p className="truncate text-sm font-medium">{user.username}</p>
            <p className="text-xs text-muted">{role}</p>
          </div>
          {placement === "down" && (
            <Link role="menuitem" href="/settings/" onClick={() => setOpen(false)} className={item}>
              <Icon name="settings" size={16} className="text-muted" />Settings
            </Link>
          )}
          {placement === "down" && user.role === "Admin" && (
            <Link role="menuitem" href="/import/" onClick={() => setOpen(false)} className={item}>
              <Icon name="folder" size={16} className="text-muted" />Library import
            </Link>
          )}
          <Link role="menuitem" href="/settings/#account" onClick={() => setOpen(false)} className={item}>
            <Icon name="user" size={16} className="text-muted" />Account
          </Link>
          <button role="menuitem" onClick={logout} className={item}>
            <Icon name="logout" size={16} className="text-muted" />Sign out
          </button>
        </div>
      )}
    </div>
  );
}
