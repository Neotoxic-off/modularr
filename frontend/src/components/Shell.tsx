"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { createContext, useContext, useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { Ambient } from "@/components/Ambient";
import { useSession } from "@/components/AuthGate";
import { ActivityProvider, useActivity } from "@/components/Activity";
import { NotificationPanel } from "@/components/ActivityFeed";
import { AddDialogProvider } from "@/components/AddDialog";
import { Icon } from "@/components/icons";
import { navGroups as groups, pages, settingsItem as settings, tabs } from "@/components/nav";
import type { NavItem } from "@/components/nav";
import { SearchPalette } from "@/components/SearchPalette";
import { ToastProvider } from "@/components/Toasts";
import { Count, IconButton, Logo, Modal } from "@/components/ui";

const shortcuts = pages.filter((i): i is NavItem & { shortcut: string } => !!i.shortcut);

const libraryPaths = ["/movies/", "/series/", "/anime/"];

const isCurrent = (pathname: string, href: string) => (href === "/" ? pathname === "/" : pathname.startsWith(href));

const pageTitle = (pathname: string) =>
  pages.find((i) => i.href !== "/" && pathname.startsWith(i.href))?.label ?? (pathname === "/" ? "Today" : "");

interface Chrome {
  backdrop?: string | null;
  // The title page shows "Series / Frieren" in the header.
  crumb?: { label: string; href: string; title: string };
}

const ChromeContext = createContext<(chrome: Chrome) => void>(() => undefined);

// A page sets the ambient backdrop and header breadcrumb while it is shown.
export function useChrome({ backdrop, crumb }: Chrome) {
  const set = useContext(ChromeContext);
  const label = crumb?.label;
  const href = crumb?.href;
  const title = crumb?.title;
  useEffect(() => {
    set({ backdrop, crumb: label && href && title ? { label, href, title } : undefined });
    return () => set({});
  }, [set, backdrop, label, href, title]);
}

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
  const router = useRouter();
  const [chrome, setChrome] = useState<Chrome>({});
  const [searching, setSearching] = useState(false);
  const [notifying, setNotifying] = useState(false);
  const [cheatSheet, setCheatSheet] = useState(false);
  const first = useRef(true);

  // Moves focus to the new page's heading so screen readers start there; the first load keeps the browser's focus.
  useEffect(() => {
    if (first.current) { first.current = false; return; }
    const frame = requestAnimationFrame(() => document.querySelector<HTMLElement>("main h1")?.focus({ preventScroll: true }));
    return () => cancelAnimationFrame(frame);
  }, [pathname]);

  useEffect(() => {
    let chord = false;
    let chordTimer: ReturnType<typeof setTimeout> | undefined;
    const key = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setSearching((s) => !s);
        return;
      }
      const target = e.target as HTMLElement;
      if (e.ctrlKey || e.metaKey || e.altKey || target.closest("input, textarea, select, [contenteditable], [role=dialog]")) return;
      if (chord) {
        chord = false;
        const item = shortcuts.find((s) => s.shortcut === e.key);
        if (item) { e.preventDefault(); router.push(item.href); }
        return;
      }
      if (e.key === "g") { chord = true; clearTimeout(chordTimer); chordTimer = setTimeout(() => { chord = false; }, 1200); }
      else if (e.key === "/") { e.preventDefault(); setSearching(true); }
      else if (e.key === "n") setNotifying(true);
      else if (e.key === "?") setCheatSheet(true);
    };
    window.addEventListener("keydown", key);
    return () => { window.removeEventListener("keydown", key); clearTimeout(chordTimer); };
  }, [router]);

  return (
    <ChromeContext value={setChrome}>
      <Ambient src={chrome.backdrop ?? null} />
      <div id="app" className="flex min-h-dvh">
        <a href="#content" className="sr-only z-50 rounded-pill bg-white px-4 py-2.5 font-semibold text-canvas focus:not-sr-only focus:fixed focus:top-3 focus:left-3">Skip to content</a>
        <Rail pathname={pathname} />
        <div className="flex min-w-0 flex-1 flex-col md:pr-3">
          <Header pathname={pathname} crumb={chrome.crumb} onSearch={() => setSearching(true)} onNotifications={() => setNotifying(true)} />
          <OfflineBanner />
          <main id="content" className="mx-auto w-full max-w-[1600px] min-w-0 flex-1 p-4 pb-28 md:pb-8 lg:p-5">{children}</main>
        </div>
        <BottomBar pathname={pathname} />
      </div>
      {searching && <SearchPalette onClose={() => setSearching(false)} />}
      {notifying && <NotificationPanel onClose={() => setNotifying(false)} />}
      {cheatSheet && <CheatSheet onClose={() => setCheatSheet(false)} />}
    </ChromeContext>
  );
}

function Rail({ pathname }: { pathname: string }) {
  const { user } = useSession();
  const { activeDownloads } = useActivity();
  return (
    <aside className="sticky top-0 hidden h-dvh shrink-0 p-3 md:block">
      <div className="glass flex h-full w-[72px] flex-col rounded-panel px-3 py-4 xl:w-60">
        <Link href="/" aria-label="modularr, Today" className="mb-5 flex h-11 items-center rounded-pill px-1.5 max-xl:justify-center max-xl:px-0">
          <span className="xl:hidden"><Logo markOnly /></span>
          <span className="max-xl:hidden"><Logo /></span>
        </Link>
        <nav aria-label="Main" className="flex flex-1 flex-col gap-4 overflow-y-auto">
          {groups.map((group) => (
            <div key={group.label ?? "home"} className="flex flex-col gap-1">
              {group.label && (
                <>
                  <p className="caption px-3.5 pb-1 max-xl:hidden">{group.label}</p>
                  <span aria-hidden="true" className="mx-2 mb-1 h-px bg-hair-2 xl:hidden" />
                </>
              )}
              {group.items.filter((i) => !i.admin || user.role === "Admin").map((item) => (
                <RailItem key={item.href} item={item} current={isCurrent(pathname, item.href)} count={item.downloads ? activeDownloads : 0} />
              ))}
            </div>
          ))}
        </nav>
        <div className="mt-3 border-t border-hair-2 pt-3">
          <RailItem item={settings} current={pathname.startsWith(settings.href)} count={0} />
        </div>
      </div>
    </aside>
  );
}

function RailItem({ item, current, count }: { item: NavItem; current: boolean; count: number }) {
  return (
    <Link href={item.href} aria-current={current ? "page" : undefined} title={item.label}
      aria-keyshortcuts={item.shortcut ? `g ${item.shortcut}` : undefined}
      className="relative flex h-11 items-center gap-3 rounded-pill px-3.5 text-sm font-medium text-ink-2 transition hover:bg-fill hover:text-ink aria-[current=page]:bg-fill-2 aria-[current=page]:text-ink max-xl:justify-center max-xl:px-0">
      <Icon name={item.icon} size={19} className="shrink-0" />
      <span className="flex-1 truncate max-xl:sr-only">{item.label}</span>
      {count > 0 && (
        <>
          <span className="text-[12px] font-semibold text-live tabular-nums max-xl:hidden">{count}<span className="sr-only"> active</span></span>
          <span aria-hidden="true" className="absolute top-1.5 right-1.5 size-2 rounded-full bg-live xl:hidden" />
        </>
      )}
    </Link>
  );
}

function Header({ pathname, crumb, onSearch, onNotifications }: { pathname: string; crumb?: Chrome["crumb"]; onSearch: () => void; onNotifications: () => void }) {
  const { unread } = useActivity();
  const [scrolled, setScrolled] = useState(false);
  const title = pageTitle(pathname);

  // The page title joins the header once the page's own heading has scrolled away.
  useEffect(() => {
    const check = () => {
      const heading = document.querySelector("main h1");
      setScrolled(!!heading && heading.getBoundingClientRect().bottom < 72);
    };
    check();
    window.addEventListener("scroll", check, { passive: true });
    return () => window.removeEventListener("scroll", check);
  }, [pathname]);

  return (
    <header data-scrolled={scrolled || !!crumb || undefined}
      className="glass sticky top-0 z-30 flex h-[calc(56px+env(safe-area-inset-top))] items-center gap-2 rounded-none border-x-0 border-t-0 px-3 pt-[env(safe-area-inset-top)] transition-[background-color,border-color,box-shadow,backdrop-filter] duration-300 md:top-3 md:mt-3 md:h-16 md:rounded-panel md:border md:px-3 md:pl-5 md:not-data-scrolled:border-transparent md:not-data-scrolled:bg-transparent md:not-data-scrolled:shadow-none md:not-data-scrolled:backdrop-blur-none">
      <Link href="/" aria-label="modularr, Today" className="rounded-pill md:hidden"><Logo /></Link>
      <div className="min-w-0 flex-1 max-md:hidden">
        {crumb ? (
          <p className="flex min-w-0 items-baseline gap-2 font-serif text-[24px] leading-none">
            <Link href={crumb.href} className="shrink-0 text-ink-2 hover:text-ink">{crumb.label}</Link>
            <span aria-hidden="true" className="text-ink-3">/</span>
            <span className="truncate">{crumb.title}</span>
          </p>
        ) : (
          <p aria-hidden="true" className={`truncate font-serif text-[24px] leading-none transition duration-300 ${scrolled ? "opacity-100" : "translate-y-1 opacity-0"}`}>{title}</p>
        )}
      </div>
      <div className="ml-auto flex items-center gap-2">
        <button onClick={onSearch} aria-keyshortcuts="Control+K Meta+K /"
          className="glass flex h-11 cursor-pointer items-center gap-2.5 rounded-pill px-4 text-sm font-medium text-ink-2 transition hover:bg-fill-2 hover:text-ink max-md:w-11 max-md:justify-center max-md:px-0 md:w-64 lg:w-80">
          <Icon name="search" size={18} className="shrink-0" />
          <span className="flex-1 text-left max-md:sr-only">Search or add…</span>
          <kbd className="rounded-chip bg-fill px-1.5 py-0.5 font-sans text-[11px] text-ink-3 max-md:hidden">⌘K</kbd>
        </button>
        <IconButton icon="bell" label={unread ? `Notifications, ${unread} unread` : "Notifications"} badge={unread} onClick={onNotifications} aria-keyshortcuts="n" />
        <UserMenu />
      </div>
    </header>
  );
}

function BottomBar({ pathname }: { pathname: string }) {
  const { activeDownloads } = useActivity();
  return (
    <nav aria-label="Main" className="glass fixed inset-x-0 bottom-0 z-30 mx-3 mb-[max(12px,env(safe-area-inset-bottom))] grid h-16 grid-cols-5 rounded-[26px] px-1 md:hidden">
      {tabs.map((tab) => {
        const current = tab.label === "Library" ? libraryPaths.some((p) => pathname.startsWith(p)) : isCurrent(pathname, tab.href);
        return (
          <Link key={tab.href} href={tab.href} aria-current={current ? "page" : undefined}
            className="group flex flex-col items-center justify-center gap-0.5 rounded-[22px] text-[11px] font-semibold text-ink-3 transition aria-[current=page]:text-ink">
            <span className="relative grid h-8 w-12 place-items-center rounded-pill transition group-aria-[current=page]:bg-fill-2">
              <Icon name={tab.icon} size={20} />
              {tab.downloads && activeDownloads > 0 && <Count value={activeDownloads} className="absolute -top-1 right-0 bg-live" />}
            </span>
            {tab.label}
          </Link>
        );
      })}
    </nav>
  );
}

function UserMenu() {
  const { user, logout } = useSession();
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const role = user.role === "Admin" ? "Administrator" : "Member";

  useEffect(() => {
    if (!open) return;
    const outside = (e: MouseEvent) => { if (!root.current?.contains(e.target as Node)) setOpen(false); };
    const escape = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      setOpen(false);
      root.current?.querySelector("button")?.focus();
    };
    document.addEventListener("mousedown", outside);
    document.addEventListener("keydown", escape);
    root.current?.querySelector<HTMLElement>("[role=menuitem]")?.focus();
    return () => {
      document.removeEventListener("mousedown", outside);
      document.removeEventListener("keydown", escape);
    };
  }, [open]);

  const item = "flex min-h-11 w-full cursor-pointer items-center gap-3 rounded-ctl px-3 text-sm font-medium text-ink-2 hover:bg-fill-2 hover:text-ink";
  const close = () => setOpen(false);

  return (
    <div ref={root} className="relative">
      <button onClick={() => setOpen(!open)} aria-haspopup="menu" aria-expanded={open} aria-label={`Account menu for ${user.username}`}
        className="grid size-11 cursor-pointer place-items-center rounded-full bg-white font-serif text-[22px] leading-none text-canvas uppercase transition hover:bg-white/85">
        {user.username[0]}
      </button>
      {open && (
        <div role="menu" aria-label="Account" className="glass-pop absolute top-full right-0 z-50 mt-2 w-64 animate-rise rounded-tile p-2">
          <div className="mb-1 border-b border-hair-2 px-3 pt-2 pb-3">
            <p className="truncate text-[15px] font-semibold">{user.username}</p>
            <p className="text-[13px] text-ink-3">{role}</p>
          </div>
          <Link role="menuitem" href="/settings/#account" onClick={close} className={item}><Icon name="user" size={17} />Account</Link>
          <Link role="menuitem" href="/settings/" onClick={close} className={`${item} md:hidden`}><Icon name="settings" size={17} />Settings</Link>
          {user.role === "Admin" && <Link role="menuitem" href="/import/" onClick={close} className={`${item} md:hidden`}><Icon name="folder" size={17} />Import</Link>}
          <button role="menuitem" onClick={logout} className={item}><Icon name="logout" size={17} />Sign out</button>
        </div>
      )}
    </div>
  );
}

const OFFLINE_GRACE_MS = 4000;

// The live stream reconnects with backoff on its own; this only says so when it stays down.
function OfflineBanner() {
  const { connected } = useActivity();
  const [offline, setOffline] = useState(false);
  useEffect(() => {
    if (connected) return;
    const timer = setTimeout(() => setOffline(!document.hidden), OFFLINE_GRACE_MS);
    return () => { clearTimeout(timer); setOffline(false); };
  }, [connected]);
  if (!offline) return null;
  return (
    <p role="status" className="glass mx-4 mt-3 flex animate-rise items-center gap-3 rounded-pill px-4 py-2.5 text-[13px] font-medium lg:mx-5">
      <span className="size-2 animate-pulse-dot rounded-full bg-warn" />Can’t reach modularr server. Retrying…
    </p>
  );
}

function CheatSheet({ onClose }: { onClose: () => void }) {
  const rows: [string, string][] = [
    ["⌘K or /", "Search or add"],
    ...shortcuts.map((s): [string, string] => [`g ${s.shortcut}`, s.label === "Movies" ? "Library" : s.label]),
    ["n", "Notifications"],
    ["?", "This list"],
    ["Esc", "Close"],
  ];
  return (
    <Modal onClose={onClose} title="Keyboard shortcuts">
      <dl className="grid grid-cols-[auto_1fr] gap-x-5 gap-y-2 px-5 pb-6 text-sm">
        {rows.map(([keys, label]) => (
          <div key={keys} className="contents">
            <dt><kbd className="well rounded-chip px-2 py-1 font-sans text-[12px] font-semibold">{keys}</kbd></dt>
            <dd className="self-center text-ink-2">{label}</dd>
          </div>
        ))}
      </dl>
    </Modal>
  );
}
