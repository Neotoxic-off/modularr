"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import type { IconName } from "@/components/icons";

const links: readonly { href: string; label: string; icon: IconName }[] = [
  { href: "/", label: "Films", icon: "film" },
  { href: "/series/", label: "Séries", icon: "tv" },
  { href: "/anime/", label: "Anime", icon: "sparkles" },
  { href: "/downloads/", label: "Téléchargements", icon: "download" },
  { href: "/indexers/", label: "Indexers", icon: "globe" },
  { href: "/settings/", label: "Réglages", icon: "settings" },
];

const item = "flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-muted transition hover:bg-raised hover:text-foreground aria-[current=page]:bg-raised aria-[current=page]:text-foreground";

export function Nav() {
  const pathname = usePathname();
  const { user, logout } = useSession();

  return (
    <>
      <aside className="sticky top-0 hidden h-screen w-60 shrink-0 flex-col border-r border-line bg-surface/50 p-4 md:flex">
        <p className="mb-6 px-3 text-lg font-semibold tracking-tight">
          modularr<span className="text-accent">.</span>
        </p>
        <nav className="flex flex-1 flex-col gap-1">
          {links.map(({ href, label, icon }) => (
            <Link key={href} href={href} aria-current={pathname === href ? "page" : undefined} className={item}>
              <Icon name={icon} />
              {label}
            </Link>
          ))}
        </nav>
        <div className="flex items-center gap-3 border-t border-line px-1 pt-4">
          <span className="flex size-8 items-center justify-center rounded-full bg-accent/20 text-sm font-semibold text-accent uppercase">
            {user.username[0]}
          </span>
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-medium">{user.username}</p>
            <p className="text-xs text-muted">{user.role === "Admin" ? "Administrateur" : "Utilisateur"}</p>
          </div>
          <button onClick={logout} aria-label="Se déconnecter" title="Se déconnecter" className="rounded-md p-1.5 text-muted hover:bg-raised hover:text-foreground">
            <Icon name="logout" />
          </button>
        </div>
      </aside>

      <nav className="fixed inset-x-0 bottom-0 z-30 flex justify-around border-t border-line bg-surface/95 px-2 py-1.5 backdrop-blur md:hidden">
        {links.map(({ href, label, icon }) => (
          <Link key={href} href={href} aria-label={label} aria-current={pathname === href ? "page" : undefined}
            className="rounded-lg p-2.5 text-muted aria-[current=page]:bg-raised aria-[current=page]:text-foreground">
            <Icon name={icon} width={20} height={20} />
          </Link>
        ))}
      </nav>
    </>
  );
}
