import type { IconName } from "@/components/icons";
import type { Managed } from "@/lib/api";

export type NavItem = { href: string; label: string; icon: IconName; shortcut?: string; admin?: boolean; downloads?: boolean };

// Labels match each page's own title.
export const navItems: readonly NavItem[] = [
  { href: "/", label: "Dashboard", icon: "home", shortcut: "t" },
  { href: "/calendar/", label: "Calendar", icon: "calendar", shortcut: "c" },
  { href: "/explore/", label: "Explore", icon: "compass", shortcut: "e", admin: true },
  { href: "/movies/", label: "Movies", icon: "film", shortcut: "l" },
  { href: "/series/", label: "Series", icon: "tv" },
  { href: "/anime/", label: "Anime", icon: "sparkles" },
  { href: "/downloads/", label: "Downloads", icon: "download", shortcut: "d", downloads: true },
  { href: "/subtitles/", label: "Subtitles", icon: "subtitles" },
];

// Members manage only their account, which the user menu links to.
export const settingsItem: NavItem = { href: "/settings/", label: "Settings", icon: "settings", shortcut: "s", admin: true };

export type AppLink = { href: string; label: string; icon: IconName };

// The app switcher: the linkarr hub and the apps it links, opened in a new tab; none when modularr is not managed.
export const appLinks = (managed: Managed | null): readonly AppLink[] => {
  if (!managed) return [];
  const hub = managed.url.replace(/\/+$/, "");
  return [
    { href: hub, label: "linkarr", icon: "globe" },
    { href: `${hub}/go/app/visularr`, label: "visularr", icon: "play" },
  ];
};

// The phone's Library tab stands for Movies, Series and Anime together.
export const tabs: readonly NavItem[] = [
  { href: "/", label: "Dashboard", icon: "home" },
  { href: "/explore/", label: "Explore", icon: "compass", admin: true },
  { href: "/movies/", label: "Library", icon: "layers" },
  { href: "/downloads/", label: "Downloads", icon: "download", downloads: true },
  { href: "/calendar/", label: "Calendar", icon: "calendar" },
];

// Every page the palette can open and the header can title, including ones off the side bar.
export const pages: readonly NavItem[] = [
  ...navItems,
  { href: "/history/", label: "History", icon: "bell" },
  { href: "/import/", label: "Library import", icon: "folder", admin: true },
  settingsItem,
];
