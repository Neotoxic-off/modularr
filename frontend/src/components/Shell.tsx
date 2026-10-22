"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import { useSession } from "@/components/AuthGate";
import { AddDialogProvider } from "@/components/AddDialog";
import { Icon } from "@/components/icons";
import type { IconName } from "@/components/icons";
import { SearchPalette } from "@/components/SearchPalette";
import { Logo } from "@/components/ui";

const links: readonly { href: string; label: string; icon: IconName }[] = [
  { href: "/", label: "Discover", icon: "compass" },
  { href: "/movies/", label: "Movies", icon: "film" },
  { href: "/series/", label: "Series", icon: "tv" },
  { href: "/anime/", label: "Anime", icon: "sparkles" },
  { href: "/downloads/", label: "Downloads", icon: "download" },
  { href: "/settings/", label: "Settings", icon: "settings" },
];

export function Shell({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const { user, logout } = useSession();
  const [searching, setSearching] = useState(false);
  const isMac = typeof navigator !== "undefined" && /Mac|iPhone|iPad/.test(navigator.platform);

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

  const current = (href: string) => (href === "/" ? pathname === "/" : pathname.startsWith(href)) ? "page" : undefined;

  return (
    <AddDialogProvider>
      <div className="flex min-h-screen">
        <aside className="sticky top-0 hidden h-screen w-60 shrink-0 flex-col border-r border-line/70 bg-surface/40 px-3 py-5 lg:flex">
          <Link href="/" className="mb-6 px-3"><Logo /></Link>
          <button onClick={() => setSearching(true)}
            className="mb-5 flex h-9 cursor-pointer items-center gap-2 rounded-lg border border-line bg-background/60 px-3 text-sm text-muted transition hover:border-muted/40 hover:text-foreground">
            <Icon name="search" size={16} />
            <span className="flex-1 text-left">Search</span>
            <kbd className="rounded border border-line px-1 text-[10px]" suppressHydrationWarning>{isMac ? "⌘" : "Ctrl"} K</kbd>
          </button>
          <nav aria-label="Main" className="flex flex-1 flex-col gap-0.5">
            {links.map(({ href, label, icon }) => (
              <Link key={href} href={href} aria-current={current(href)}
                className="group relative flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-muted transition hover:bg-raised/70 hover:text-foreground aria-[current=page]:bg-raised aria-[current=page]:text-foreground">
                <span className="absolute top-2 bottom-2 left-0 w-0.5 rounded-full bg-accent opacity-0 transition group-aria-[current=page]:opacity-100" />
                <Icon name={icon} />
                {label}
              </Link>
            ))}
          </nav>
          <div className="flex items-center gap-3 rounded-xl border border-line/70 bg-background/40 p-2">
            <span className="grid size-8 place-items-center rounded-full bg-accent/15 text-sm font-semibold text-accent uppercase">{user.username[0]}</span>
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-medium">{user.username}</p>
              <p className="text-xs text-muted">{user.role === "Admin" ? "Administrator" : "Member"}</p>
            </div>
            <button onClick={logout} aria-label="Sign out" title="Sign out" className="cursor-pointer rounded-md p-1.5 text-muted hover:bg-raised hover:text-foreground">
              <Icon name="logout" size={16} />
            </button>
          </div>
        </aside>

        <div className="flex min-w-0 flex-1 flex-col">
          <header className="sticky top-0 z-30 flex h-14 items-center gap-3 border-b border-line/70 bg-background/80 px-4 backdrop-blur-xl lg:hidden">
            <Link href="/" className="mr-auto"><Logo /></Link>
            <button onClick={() => setSearching(true)} aria-label="Search" className="cursor-pointer rounded-lg p-2 text-muted hover:bg-raised hover:text-foreground">
              <Icon name="search" />
            </button>
            <button onClick={logout} aria-label="Sign out" className="cursor-pointer rounded-lg p-2 text-muted hover:bg-raised hover:text-foreground">
              <Icon name="logout" />
            </button>
          </header>
          <main key={pathname} className="w-full max-w-[1600px] min-w-0 flex-1 animate-fade px-4 pt-6 pb-28 sm:px-6 lg:px-10 lg:pt-8 lg:pb-12">{children}</main>
        </div>

        <nav aria-label="Main" className="fixed inset-x-0 bottom-0 z-30 grid grid-cols-6 border-t border-line/70 bg-background/90 px-1 pt-1.5 pb-[max(0.375rem,env(safe-area-inset-bottom))] backdrop-blur-xl lg:hidden">
          {links.map(({ href, label, icon }) => (
            <Link key={href} href={href} aria-current={current(href)}
              className="flex flex-col items-center gap-0.5 rounded-lg py-1 text-[10px] text-muted aria-[current=page]:text-accent">
              <Icon name={icon} size={20} />
              {label}
            </Link>
          ))}
        </nav>
      </div>
      {searching && <SearchPalette onClose={() => setSearching(false)} />}
    </AddDialogProvider>
  );
}
