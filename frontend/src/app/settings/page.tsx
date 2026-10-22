"use client";

import { useEffect, useState } from "react";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import type { IconName } from "@/components/icons";
import { Account, Folders, Indexers, Users } from "@/components/settings/General";
import { MediaSettings } from "@/components/settings/MediaSettings";
import { Profiles } from "@/components/settings/Profiles";
import { PageHeader } from "@/components/ui";

type Tab = "media" | "profiles" | "folders" | "indexers" | "users" | "account";

const tabs: readonly { value: Tab; label: string; icon: IconName; admin: boolean }[] = [
  { value: "media", label: "Media management", icon: "languages", admin: true },
  { value: "profiles", label: "Quality profiles", icon: "sliders", admin: true },
  { value: "folders", label: "Folders", icon: "folder", admin: true },
  { value: "indexers", label: "Indexers", icon: "globe", admin: false },
  { value: "users", label: "Users", icon: "users", admin: true },
  { value: "account", label: "Account", icon: "key", admin: false },
];

export default function SettingsPage() {
  const { user } = useSession();
  const isAdmin = user.role === "Admin";
  const available = tabs.filter((t) => !t.admin || isAdmin);
  const [tab, setTab] = useState<Tab>(isAdmin ? "media" : "indexers");

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
      <div className="grid gap-8 lg:grid-cols-[220px_minmax(0,1fr)]">
        <nav aria-label="Settings" className="rail -mx-4 flex gap-1 overflow-x-auto px-4 lg:mx-0 lg:flex-col lg:px-0">
          {available.map((t) => (
            <button key={t.value} onClick={() => select(t.value)} aria-current={tab === t.value ? "page" : undefined}
              className="flex shrink-0 cursor-pointer items-center gap-2.5 rounded-lg px-3 py-2 text-sm text-muted transition hover:bg-raised/70 hover:text-foreground aria-[current=page]:bg-raised aria-[current=page]:text-foreground">
              <Icon name={t.icon} size={16} />{t.label}
            </button>
          ))}
        </nav>
        <div key={tab} className="min-w-0 animate-fade">
          {tab === "media" && <MediaSettings />}
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
