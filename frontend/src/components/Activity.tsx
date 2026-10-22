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
  Regrabbed: "refresh", Imported: "check", ImportFailed: "alert", Removed: "trash", SubtitleDownloaded: "languages", SubtitlesMissing: "languages",
  ScanCompleted: "folder",
};

// What each event is called on the timeline, and the status colour it carries.
export const eventLook: Record<ActivityKind, { label: string; tone: Tone }> = {
  Added: { label: "Added", tone: "neutral" },
  SearchStarted: { label: "Searching", tone: "seek" },
  SearchCompleted: { label: "Found", tone: "seek" },
  NothingFound: { label: "Nothing found", tone: "warn" },
  SearchFailed: { label: "Search failed", tone: "bad" },
  Grabbed: { label: "Grabbed", tone: "live" },
  DownloadStarted: { label: "Downloading", tone: "live" },
  DownloadStalled: { label: "Stalled", tone: "warn" },
  DownloadFailed: { label: "Failed", tone: "bad" },
  Blocklisted: { label: "Blocklisted", tone: "bad" },
  Regrabbed: { label: "Grabbed again", tone: "live" },
  Imported: { label: "Imported", tone: "ok" },
  ImportFailed: { label: "Import failed", tone: "bad" },
  Removed: { label: "Removed", tone: "neutral" },
  SubtitleDownloaded: { label: "Subtitles", tone: "subs" },
  SubtitlesMissing: { label: "Subtitles missing", tone: "subs" },
  ScanCompleted: { label: "Scanned", tone: "neutral" },
};

// A removed title's page no longer loads.
export const activityHref = (a: Pick<Activity, "kind" | "movieId" | "seriesId">) =>
  a.kind === "Removed" ? null : a.movieId !== null ? titleHref("movie", a.movieId) : a.seriesId !== null ? titleHref("series", a.seriesId) : null;

// Toasts about the same title replace each other.
export const titleKey = (a: Pick<Activity, "movieId" | "seriesId">) =>
  a.movieId !== null ? `movie:${a.movieId}` : a.seriesId !== null ? `series:${a.seriesId}` : null;

export type Scope = { seriesId?: number; movieId?: number };

const inScope = (a: Activity, { seriesId, movieId }: Scope) =>
  (seriesId === undefined || a.seriesId === seriesId) && (movieId === undefined || a.movieId === movieId);

const toastKinds: ReadonlySet<ActivityKind> = new Set(["Grabbed", "Imported", "NothingFound", "DownloadFailed"]);

const isActive = (d: Download) => d.state === "Downloading" || d.state === "ImportPending" || d.state === "Importing";

const RETRY_MAX_MS = 30000;
const SETTLE_MS = 300;
// Progress frames stop when the last download finishes; older numbers would then look frozen.
const PROGRESS_STALE_MS = 5000;

// Runs fn once a burst of calls (a search's started, grabbed and completed events) has settled.
function settled(fn: () => void) {
  let timer: ReturnType<typeof setTimeout> | undefined;
  return {
    run: () => { clearTimeout(timer); timer = setTimeout(fn, SETTLE_MS); },
    cancel: () => clearTimeout(timer),
  };
}

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
      if (document.hidden) return;
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
        // The stream cannot report a 401, this call sends an expired session to sign in.
        refreshUnread();
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

    // Hidden tabs let go of their stream: over HTTP/1.1 the browser allows only 6 connections per origin, shared by every tab.
    const onVisibility = () => {
      clearTimeout(retry);
      source?.close();
      source = null;
      setConnected(false);
      delay = 1000;
      connect();
    };

    connect();
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      document.removeEventListener("visibilitychange", onVisibility);
      clearTimeout(retry);
      clearTimeout(stale);
      source?.close();
    };
  }, [refreshUnread]);

  useEffect(() => {
    refreshUnread();
    countActive();
  }, [epoch, refreshUnread, countActive]);

  useEffect(() => {
    const active = settled(countActive);
    const unsubscribe = subscribe((a) => {
      if (a.downloadId !== null) active.run();
      if (toastKinds.has(a.kind) || a.level === "Error") {
        toast({ key: titleKey(a) ?? `activity:${a.id}`, icon: kindIcon[a.kind], tone: a.level === "Error" ? "bad" : eventLook[a.kind].tone, title: a.title, message: a.message, href: activityHref(a) });
      }
    });
    return () => { unsubscribe(); active.cancel(); };
  }, [subscribe, countActive, toast]);

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

  return { items, setItems, more, loadMore, error, reload: () => load() };
}

// Runs onEvent once a burst of matching live activities has settled.
export function useActivityRefresh(match: (activity: Activity) => boolean, onEvent: () => void) {
  const { subscribe } = useActivity();
  const latest = useRef({ match, onEvent });
  useEffect(() => { latest.current = { match, onEvent }; });
  useEffect(() => {
    const refresh = settled(() => latest.current.onEvent());
    const unsubscribe = subscribe((a) => { if (latest.current.match(a)) refresh.run(); });
    return () => { unsubscribe(); refresh.cancel(); };
  }, [subscribe]);
}

export const useTitleEvents = (scope: Scope, onEvent: () => void) => useActivityRefresh((a) => inScope(a, scope), onEvent);
