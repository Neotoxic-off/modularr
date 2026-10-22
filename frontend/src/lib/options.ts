"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { Kind, Language, MediaSettings, Profile } from "@/lib/api";

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

export const defaultsFor = (media: MediaSettings, kind: Kind) => (kind === "movie" ? media.movies : kind === "series" ? media.series : media.anime);

export const languageLabel = (name: string) =>
  name === "Any" ? "Any language" : name === "Original" ? "Original language" : name;
