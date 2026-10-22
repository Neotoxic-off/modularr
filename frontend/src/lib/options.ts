"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { Category, Kind, Language, MediaSettings, Profile } from "@/lib/api";

export interface Options {
  profiles: Profile[];
  languages: Language[];
  media: MediaSettings;
}

// Loaded once and shared by every dialog and page; settings saves call reloadOptions().
let cache: Promise<Options> | null = null;
const listeners = new Set<(options: Options) => void>();

const load = () => Promise.all([api.profiles(), api.languages(), api.media()]).then(([profiles, languages, media]) => ({ profiles, languages, media }));

export function reloadOptions() {
  cache = load();
  void cache.then((options) => listeners.forEach((listener) => listener(options)), () => undefined);
}

export function useOptions(): Options | null {
  const [options, setOptions] = useState<Options | null>(null);
  useEffect(() => {
    cache ??= load();
    cache.then(setOptions, () => { cache = null; });
    listeners.add(setOptions);
    return () => { listeners.delete(setOptions); };
  }, []);
  return options;
}

export const categoryOf = (kind: Kind): Category => (kind === "movie" ? "Movie" : kind === "series" ? "Series" : "Anime");

export const defaultsFor = (media: MediaSettings, kind: Kind) => (kind === "movie" ? media.movies : kind === "series" ? media.series : media.anime);

// Mirrors MediaOptions.WantedLanguage without the item's own language: first matching exception, category, default.
export function policyLanguage(media: MediaSettings, kind: Kind, originalLanguage: string | null): string {
  const category = categoryOf(kind);
  const rule = media.exceptions.find((r) =>
    (r.category === null || r.category === category) && (r.originalLanguage === null || r.originalLanguage === originalLanguage));
  return rule?.language ?? defaultsFor(media, kind).language ?? media.defaultLanguage;
}

export const languageLabel = (name: string) =>
  name === "Any" ? "Any language" : name === "Original" ? "Original language" : name;
