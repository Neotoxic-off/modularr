import type { IconName } from "@/components/icons";

export type NavItem = { href: string; label: string; icon: IconName; shortcut?: string; admin?: boolean; downloads?: boolean };

export const navGroups: readonly { label: string | null; items: readonly NavItem[] }[] = [
  { label: null, items: [
    { href: "/", label: "Today", icon: "home", shortcut: "t" },
    { href: "/calendar/", label: "Calendar", icon: "calendar", shortcut: "c" },
  ] },
  { label: "Library", items: [
    { href: "/movies/", label: "Movies", icon: "film", shortcut: "l" },
    { href: "/series/", label: "Series", icon: "tv" },
    { href: "/anime/", label: "Anime", icon: "sparkles" },
  ] },
  { label: "Discover", items: [{ href: "/explore/", label: "Explore", icon: "compass", shortcut: "e" }] },
  { label: "Activity", items: [
    { href: "/downloads/", label: "Downloads", icon: "download", shortcut: "d", downloads: true },
    { href: "/import/", label: "Import", icon: "folder", admin: true },
  ] },
];

export const settingsItem: NavItem = { href: "/settings/", label: "Settings", icon: "settings", shortcut: "s" };

export const tabs: readonly NavItem[] = [
  { href: "/", label: "Today", icon: "home" },
  { href: "/movies/", label: "Library", icon: "layers" },
  { href: "/explore/", label: "Explore", icon: "compass" },
  { href: "/downloads/", label: "Downloads", icon: "download", downloads: true },
  { href: "/calendar/", label: "Calendar", icon: "calendar" },
];

export const pages: readonly NavItem[] = [...navGroups.flatMap((g) => g.items), settingsItem];
