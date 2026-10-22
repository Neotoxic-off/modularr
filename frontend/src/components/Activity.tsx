"use client";

import { createContext, useCallback, useContext, useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { api, errorMessage, titleHref } from "@/lib/api";
import type { Activity, ActivityKind, Download, DownloadProgress } from "@/lib/api";
import type { IconName } from "@/components/icons";
import { useToast } from "@/components/Toasts";
import type { Tone } from "@/components/ui";

type Listener = (activity: Activity) => void;

interface ActivityState {
  connected: boolean;
  // Bumps on every (re)connect so views refetch what they may have missed while the stream was down.
  epoch: number;
  unread: number;
  activeDownloads: number;
  progress: ReadonlyMap<number, DownloadProgress>;
  subscribe: (listener: Listener) => () => void;
  markRead: (upTo?: number) => Promise<void>;
}

const ActivityContext = createContext<ActivityState | null>(null);

export function useActivity(): ActivityState {
  const state = useContext(ActivityContext);
  if (!state) throw new Error("useActivity outside ActivityProvider");
  return state;
}

export const kindIcon: Record<ActivityKind, IconName> = {
  Added: "plus", SearchStarted: "search", SearchCompleted: "list", NothingFound: "searchX", SearchFailed: "alert",
  Grabbed: "download", DownloadStarted: "down", DownloadStalled: "clock", DownloadFailed: "x", Blocklisted: "ban",
  Regrabbed: "refresh", Imported: "check", ImportFailed: "alert", Removed: "trash",
};

export const levelTone: Record<Activity["level"], Tone> = { Info: "accent", Success: "green", Warning: "amber", Error: "red" };

export const activityHref = (a: Pick<Activity, "movieId" | "seriesId">) =>
  a.movieId !== null ? titleHref("movie", a.movieId) : a.seriesId !== null ? titleHref("series", a.seriesId) : null;

// Toasts about the same title replace each other.
export const titleKey = (a: Pick<Activity, "movieId" | "seriesId">) =>
  a.movieId !== null ? `movie:${a.movieId}` : a.seriesId !== null ? `series:${a.seriesId}` : null;

export type Scope = { seriesId?: number; movieId?: number };

const inScope = (a: Activity, { seriesId, movieId }: Scope) =>
  (seriesId === undefined || a.seriesId === seriesId) && (movieId === undefined || a.movieId === movieId);

const toastKinds: ReadonlySet<ActivityKind> = new Set(["Grabbed", "Imported", "NothingFound", "DownloadFailed"]);

const isActive = (d: Download) => d.state === "Downloading" || d.state === "ImportPending" || d.state === "Importing";

const RETRY_MAX_MS = 30000;
// Progress frames stop when the last download finishes; older numbers would then look frozen.
const PROGRESS_STALE_MS = 5000;

export function ActivityProvider({ children }: { children: ReactNode }) {
  const toast = useToast();
  const listeners = useRef(new Set<Listener>());
  const [connected, setConnected] = useState(false);
  const [epoch, setEpoch] = useState(0);
  const [unread, setUnread] = useState(0);
  const [activeDownloads, setActiveDownloads] = useState(0);
  const [progress, setProgress] = useState<ReadonlyMap<number, DownloadProgress>>(new Map());

  const subscribe = useCallback((listener: Listener) => {
    listeners.current.add(listener);
    return () => { listeners.current.delete(listener); };
  }, []);

  const countActive = useCallback(() => {
    api.downloads().then((d) => setActiveDownloads(d.filter(isActive).length)).catch(() => undefined);
  }, []);

  const refreshUnread = useCallback(() => {
    api.unreadCount().then((r) => setUnread(r.count)).catch(() => undefined);
  }, []);

  const markRead = useCallback(async (upTo?: number) => {
    await api.markRead(upTo);
    refreshUnread();
  }, [refreshUnread]);

  useEffect(() => {
    let source: EventSource | null = null;
    let retry: ReturnType<typeof setTimeout> | undefined;
    let stale: ReturnType<typeof setTimeout> | undefined;
    let delay = 1000;

    const connect = () => {
      source = new EventSource("/api/events");
      source.onopen = () => {
        delay = 1000;
        setConnected(true);
        setEpoch((e) => e + 1);
      };
      source.onerror = () => {
        setConnected(false);
        // The browser retries a dropped stream by itself but gives up on a refused one (a 401, a restart).
        if (source?.readyState !== EventSource.CLOSED) return;
        retry = setTimeout(connect, delay);
        delay = Math.min(delay * 2, RETRY_MAX_MS);
      };
      source.addEventListener("activity", (e: MessageEvent<string>) => {
        const activity = JSON.parse(e.data) as Activity;
        setUnread((n) => n + 1);
        listeners.current.forEach((listener) => listener(activity));
      });
      source.addEventListener("progress", (e: MessageEvent<string>) => {
        setProgress(new Map((JSON.parse(e.data) as DownloadProgress[]).map((p) => [p.id, p])));
        clearTimeout(stale);
        stale = setTimeout(() => setProgress(new Map()), PROGRESS_STALE_MS);
      });
    };

    connect();
    return () => {
      clearTimeout(retry);
      clearTimeout(stale);
      source?.close();
    };
  }, []);

  useEffect(() => {
    refreshUnread();
    countActive();
  }, [epoch, refreshUnread, countActive]);

  useEffect(() => subscribe((a) => {
    if (a.downloadId !== null) countActive();
    if (toastKinds.has(a.kind) || a.level === "Error") {
      toast({ key: titleKey(a) ?? `activity:${a.id}`, icon: kindIcon[a.kind], tone: levelTone[a.level], title: a.title, message: a.message, href: activityHref(a) });
    }
  }), [subscribe, countActive, toast]);

  return (
    <ActivityContext value={{ connected, epoch, unread, activeDownloads, progress, subscribe, markRead }}>
      {children}
    </ActivityContext>
  );
}

const PAGE = 30;

// The newest activities in a scope (everything, or one title), kept current by the live stream.
export function useActivityFeed(scope: Scope) {
  const { subscribe, epoch } = useActivity();
  const { seriesId, movieId } = scope;
  const [items, setItems] = useState<Activity[] | null>(null);
  const [more, setMore] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback((before?: number) => {
    api.activity({ seriesId, movieId, before, limit: PAGE })
      .then((page) => {
        setItems((current) => (before === undefined ? page : [...(current ?? []), ...page]));
        setMore(page.length === PAGE);
        setError(null);
      })
      .catch((e: unknown) => setError(errorMessage(e)));
  }, [seriesId, movieId]);

  useEffect(() => load(), [load, epoch]);

  useEffect(() => subscribe((a) => {
    if (inScope(a, { seriesId, movieId })) setItems((list) => list && [a, ...list.filter((x) => x.id !== a.id)]);
  }), [subscribe, seriesId, movieId]);

  const loadMore = () => {
    const last = items?.at(-1);
    if (last) load(last.id);
  };

  return { items, setItems, more, loadMore, error };
}

// Runs for every live activity about one title.
export function useTitleEvents(scope: Scope, onEvent: () => void) {
  const { subscribe } = useActivity();
  const handler = useRef(onEvent);
  useEffect(() => { handler.current = onEvent; });
  const { seriesId, movieId } = scope;
  useEffect(() => subscribe((a) => { if (inScope(a, { seriesId, movieId })) handler.current(); }), [subscribe, seriesId, movieId]);
}
