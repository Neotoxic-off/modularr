"use client";

import { useEffect, useState } from "react";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import type { IconName } from "@/components/icons";
import { Account, Folders, General, Indexers, Users } from "@/components/settings/General";
import { MediaSettings } from "@/components/settings/MediaSettings";
import { Profiles } from "@/components/settings/Profiles";
import { SubtitleSettings } from "@/components/settings/SubtitleSettings";
import { PageHeader, Select } from "@/components/ui";

type Tab = "general" | "folders" | "media" | "subtitles" | "profiles" | "indexers" | "users" | "account";

const tabs: readonly { value: Tab; label: string; icon: IconName; admin: boolean }[] = [
  { value: "general", label: "General", icon: "settings", admin: true },
  { value: "folders", label: "Folders", icon: "folder", admin: true },
  { value: "media", label: "Media", icon: "languages", admin: true },
  { value: "subtitles", label: "Subtitles", icon: "subtitles", admin: true },
  { value: "profiles", label: "Quality profiles", icon: "sliders", admin: true },
  { value: "indexers", label: "Indexers", icon: "globe", admin: false },
  { value: "users", label: "Users", icon: "users", admin: true },
  { value: "account", label: "Account", icon: "key", admin: false },
];

export default function SettingsPage() {
  const { user } = useSession();
  const isAdmin = user.role === "Admin";
  const available = tabs.filter((t) => !t.admin || isAdmin);
  const [tab, setTab] = useState<Tab>(isAdmin ? "general" : "indexers");

  // The tab lives in the URL hash so a reload or a shared link lands on the same tab.
  useEffect(() => {
    const fromHash = () => {
      const hash = window.location.hash.slice(1);
      if (tabs.some((t) => t.value === hash && (!t.admin || isAdmin))) setTab(hash as Tab);
    };
    fromHash();
    window.addEventListener("hashchange", fromHash);
    return () => window.removeEventListener("hashchange", fromHash);
  }, [isAdmin]);

  const select = (value: Tab) => {
    setTab(value);
    history.replaceState(null, "", `#${value}`);
  };

  return (
    <>
      <PageHeader title="Settings" />
      <div className="grid gap-6 lg:grid-cols-[220px_minmax(0,720px)] lg:gap-8">
        <Select value={tab} onChange={(e) => select(e.target.value as Tab)} aria-label="Settings section" className="lg:hidden">
          {available.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
        </Select>
        <nav aria-label="Settings" className="flex flex-col gap-1 max-lg:hidden">
          {available.map((t) => (
            <button key={t.value} onClick={() => select(t.value)} aria-current={tab === t.value ? "page" : undefined}
              className="flex h-11 cursor-pointer items-center gap-3 rounded-pill px-4 text-sm font-medium text-ink-2 transition hover:bg-fill hover:text-ink aria-[current=page]:bg-fill-2 aria-[current=page]:text-ink">
              <Icon name={t.icon} size={18} />{t.label}
            </button>
          ))}
        </nav>
        <div key={tab} className="min-w-0 animate-fade">
          {tab === "general" && <General />}
          {tab === "media" && <MediaSettings />}
          {tab === "subtitles" && <SubtitleSettings />}
          {tab === "profiles" && <Profiles />}
          {tab === "folders" && <Folders />}
          {tab === "indexers" && <Indexers />}
          {tab === "users" && <Users me={user} />}
          {tab === "account" && <Account me={user} />}
        </div>
      </div>
    </>
  );
}
