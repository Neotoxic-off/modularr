"use client";

import { useEffect, useState } from "react";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import type { IconName } from "@/components/icons";
import { Account } from "@/components/settings/Account";
import { Connections } from "@/components/settings/Connections";
import { Indexers } from "@/components/settings/Indexers";
import { Languages } from "@/components/settings/Languages";
import { Library } from "@/components/settings/Library";
import { Profiles } from "@/components/settings/Profiles";
import { Backups } from "@/components/settings/System";
import { Automation, TorrentSettings } from "@/components/settings/TorrentSettings";
import { PageHeader, Select } from "@/components/ui";

type Tab = "library" | "languages" | "quality" | "downloads" | "connections" | "system" | "account";

const tabs: readonly { value: Tab; label: string; icon: IconName }[] = [
  { value: "library", label: "Library", icon: "folder" },
  { value: "languages", label: "Languages", icon: "languages" },
  { value: "quality", label: "Quality", icon: "sliders" },
  { value: "downloads", label: "Downloads", icon: "download" },
  { value: "connections", label: "Connections", icon: "bell" },
  { value: "system", label: "System", icon: "settings" },
  { value: "account", label: "Account", icon: "key" },
];

// Hashes of the old tabs, so bookmarks and links keep landing on the right page.
const aliases: Record<string, Tab> = {
  general: "library", folders: "library", media: "languages", subtitles: "languages", profiles: "quality",
  indexers: "downloads", notifications: "connections", users: "account",
};

const fromHash = (): Tab | null => {
  const hash = window.location.hash.slice(1);
  return tabs.some((t) => t.value === hash) ? (hash as Tab) : aliases[hash] ?? null;
};

export default function SettingsPage() {
  const { user } = useSession();
  const isAdmin = user.role === "Admin";
  const [tab, setTab] = useState<Tab>("library");

  // The tab lives in the URL hash so a reload or a shared link lands on the same tab.
  useEffect(() => {
    const sync = () => setTab(fromHash() ?? "library");
    sync();
    window.addEventListener("hashchange", sync);
    return () => window.removeEventListener("hashchange", sync);
  }, []);

  const select = (value: Tab) => {
    setTab(value);
    history.replaceState(null, "", `#${value}`);
  };

  // Members only manage their own account.
  if (!isAdmin) return (
    <>
      <PageHeader title="Settings" />
      <Account />
    </>
  );

  return (
    <>
      <PageHeader title="Settings" />
      <div className="grid gap-6 lg:grid-cols-[220px_minmax(0,1fr)] lg:gap-8">
        <Select value={tab} onChange={(e) => select(e.target.value as Tab)} aria-label="Settings section" className="lg:hidden">
          {tabs.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
        </Select>
        <nav aria-label="Settings" className="flex flex-col gap-1 max-lg:hidden">
          {tabs.map((t) => (
            <button key={t.value} onClick={() => select(t.value)} aria-current={tab === t.value ? "page" : undefined}
              className="flex h-11 cursor-pointer items-center gap-3 rounded-pill px-4 text-sm font-medium text-ink-2 transition hover:bg-fill hover:text-ink aria-[current=page]:bg-fill-2 aria-[current=page]:text-ink">
              <Icon name={t.icon} size={20} />{t.label}
            </button>
          ))}
        </nav>
        <div key={tab} className="min-w-0 animate-fade space-y-6">
          {tab === "library" && <Library />}
          {tab === "languages" && <Languages />}
          {tab === "quality" && <Profiles />}
          {tab === "downloads" && <><TorrentSettings /><Automation /><Indexers /></>}
          {tab === "connections" && <Connections />}
          {tab === "system" && <Backups />}
          {tab === "account" && <Account />}
        </div>
      </div>
    </>
  );
}
