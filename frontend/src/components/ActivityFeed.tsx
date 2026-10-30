"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import type { ReactNode } from "react";
import { formatEta, formatSize } from "@/lib/api";
import type { Activity } from "@/lib/api";
import { dayTime, locale } from "@/lib/format";
import { activityHref, eventLook, kindIcon, titleKey, useActivity, useActivityFeed } from "@/components/Activity";
import type { Scope } from "@/components/Activity";
import { Icon } from "@/components/icons";
import { Alert, Button, Progress, Skeleton, toneText, useDialog } from "@/components/ui";

const NOTIFICATIONS_PER_PAGE = 20;

const COLLAPSE_MS = 10 * 60000;

// Events about one title within ten minutes of each other read as one row ("3 events").
export function groupEvents(items: readonly Activity[]): Activity[][] {
  const groups: Activity[][] = [];
  for (const a of items) {
    const last = groups.at(-1);
    const head = last?.[0];
    const key = titleKey(a);
    if (last && head && key !== null && titleKey(head) === key && new Date(head.time).getTime() - new Date(a.time).getTime() < COLLAPSE_MS) last.push(a);
    else groups.push([a]);
  }
  return groups;
}

const dayFormat = new Intl.DateTimeFormat(locale, { weekday: "long", day: "numeric", month: "long" });
const timeFormat = new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit" });

function dayLabel(time: Date): string {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const days = Math.round((today.getTime() - new Date(time).setHours(0, 0, 0, 0)) / 86400000);
  if (days <= 0) return new Date().getHours() >= 18 ? "Tonight" : "Today";
  if (days === 1) return "Yesterday";
  return dayFormat.format(time);
}

export function byDay(groups: Activity[][]): [string, Activity[][]][] {
  const days = new Map<string, Activity[][]>();
  for (const group of groups) {
    const head = group[0];
    if (!head) continue;
    const label = dayLabel(new Date(head.time));
    days.set(label, [...(days.get(label) ?? []), group]);
  }
  return [...days];
}

// The one row of every activity list: the home timeline, the bell and a title's history.
export function TimelineRow({ group, poster, action, showTitle = true, onNavigate }: {
  group: Activity[];
  poster?: string | null;
  action?: ReactNode;
  showTitle?: boolean;
  onNavigate?: () => void;
}) {
  const [expanded, setExpanded] = useState(false);
  const [a, ...rest] = group;
  if (!a) return null;
  const { label, tone } = eventLook[a.kind];
  const href = activityHref(a);
  return (
    <li className="group/row relative pl-6">
      <span aria-hidden="true" className="absolute -top-2.5 bottom-0 left-[5px] w-0.5 bg-hair-2 group-first/row:top-8 group-last/row:bottom-auto group-last/row:h-10.5 group-only/row:hidden" />
      <span aria-hidden="true" className={`absolute top-8 left-0 size-3 rounded-full bg-current shadow-[0_0_12px_currentColor] ${toneText[tone]}`} />
      <article className={`well flex min-h-16 gap-4 rounded-tile p-4 ${a.read ? "" : "bg-fill"}`}>
        {poster !== undefined && (
          poster
            // eslint-disable-next-line @next/next/no-img-element
            ? <img src={poster} alt="" loading="lazy" className="h-[72px] w-[52px] shrink-0 rounded-chip object-cover ring-1 ring-white/8" />
            : <span className={`grid h-[72px] w-[52px] shrink-0 place-items-center rounded-chip bg-fill ${toneText[tone]}`}><Icon name={kindIcon[a.kind]} size={20} /></span>
        )}
        <div className="min-w-0 flex-1">
          <p className="flex items-center gap-2 text-caption font-bold tracking-[.08em] uppercase">
            <span className={toneText[tone]}>{label}</span>
            <time dateTime={a.time} title={dayTime(a.time)} className="font-medium tracking-normal text-ink-3 tabular-nums normal-case">{timeFormat.format(new Date(a.time))}</time>
          </p>
          {showTitle && (href
            ? <h3 className="mt-0.5 truncate text-body font-semibold"><Link href={href} onClick={onNavigate} className="hover:underline">{a.title}</Link></h3>
            : <h3 className="mt-0.5 truncate text-body font-semibold">{a.title}</h3>)}
          {a.message && <p className="mt-0.5 line-clamp-2 text-meta leading-relaxed text-ink-2">{a.message}</p>}
          {rest.length > 0 && (
            <button onClick={() => setExpanded(!expanded)} aria-expanded={expanded}
              className="-ml-2 mt-1 inline-flex min-h-11 cursor-pointer items-center gap-1 rounded-pill px-2 text-meta font-medium text-ink-2 hover:text-ink">
              <Icon name="chevronRight" size={14} className={`transition ${expanded ? "rotate-90" : ""}`} />{group.length} events
            </button>
          )}
          {expanded && (
            <ol className="mt-1 space-y-1.5 border-l border-hair-2 pl-3">
              {rest.map((e) => (
                <li key={e.id} className="text-meta text-ink-2">
                  <span className={`font-semibold ${toneText[eventLook[e.kind].tone]}`}>{eventLook[e.kind].label}</span>
                  <span className="text-ink-3 tabular-nums"> {timeFormat.format(new Date(e.time))}</span>
                  {e.message && <span className="block line-clamp-2">{e.message}</span>}
                </li>
              ))}
            </ol>
          )}
        </div>
        {action && <div className="shrink-0 self-center">{action}</div>}
      </article>
    </li>
  );
}

// Day-grouped timeline under sticky day headers.
export function Timeline({ groups, posterOf, actionOf, showTitle, onNavigate, stickyTop = "top-0" }: {
  groups: Activity[][];
  posterOf?: (a: Activity) => string | null;
  actionOf?: (a: Activity) => ReactNode;
  showTitle?: boolean;
  onNavigate?: () => void;
  stickyTop?: string;
}) {
  return (
    <div className="space-y-4">
      {byDay(groups).map(([day, list]) => (
        <section key={day} aria-label={day}>
          <h3 className={`sticky z-10 -mx-1 mb-2 px-1 py-1.5 text-meta font-semibold text-ink-2 ${stickyTop}`}>
            <span className="glass rounded-pill px-3 py-1">{day}</span>
          </h3>
          <ol className="space-y-2.5">
            {list.map((g) => {
              const head = g[0];
              return head && <TimelineRow key={head.id} group={g} poster={posterOf?.(head)} action={actionOf?.(head)} showTitle={showTitle} onNavigate={onNavigate} />;
            })}
          </ol>
        </section>
      ))}
    </div>
  );
}

export function TimelineSkeleton({ rows = 4 }: { rows?: number }) {
  return <div className="space-y-2.5" aria-hidden="true">{Array.from({ length: rows }, (_, i) => <Skeleton key={i} className="ml-6 h-[106px] rounded-tile" />)}</div>;
}

// A sentinel that loads the next page as it scrolls into view, with a button for keyboard and screen reader users.
export function LoadMore({ onMore, label = "Load more" }: { onMore: () => void; label?: string }) {
  const sentinel = useRef<HTMLDivElement>(null);
  const latest = useRef(onMore);
  useEffect(() => { latest.current = onMore; });
  useEffect(() => {
    const node = sentinel.current;
    if (!node) return;
    const observer = new IntersectionObserver(([entry]) => { if (entry?.isIntersecting) latest.current(); }, { rootMargin: "400px" });
    observer.observe(node);
    return () => observer.disconnect();
  }, []);
  return (
    <div ref={sentinel} className="flex justify-center pt-2">
      <Button variant="ghost" size="sm" onClick={onMore}>{label}</Button>
    </div>
  );
}

const PEEK = 5;

// The bell's small dropdown (a bottom sheet on phones): the latest few events; "See all" opens the History page.
export function NotificationPeek({ onClose }: { onClose: () => void }) {
  const { unread, markRead } = useActivity();
  const { items, error } = useActivityFeed({});
  const [wasUnread] = useState(unread);
  const panel = useRef<HTMLDivElement>(null);
  useDialog(panel, onClose);
  useEffect(() => { void markRead(); }, [markRead]);

  return createPortal(
    <div className="fixed inset-0 z-50" onMouseDown={onClose}>
      <div ref={panel} role="dialog" aria-modal="true" aria-label="Notifications" tabIndex={-1} onMouseDown={(e) => e.stopPropagation()}
        className="glass-pop absolute inset-x-0 bottom-0 animate-rise overflow-hidden rounded-t-tile pb-[env(safe-area-inset-bottom)] outline-none md:inset-x-auto md:top-[68px] md:right-3 md:bottom-auto md:w-[380px] md:rounded-tile md:pb-0">
        <div className="flex items-baseline justify-between gap-3 px-4 pt-3.5 pb-2">
          <h2 className="text-body font-semibold">Notifications</h2>
          <span className="text-meta text-ink-3">{wasUnread > 0 ? `${wasUnread} new` : "All caught up"}</span>
        </div>
        <div className="max-h-[min(420px,60dvh)] overflow-y-auto px-3 pb-2">
          {error && <Alert>{error}</Alert>}
          {!items ? <TimelineSkeleton rows={2} /> : items.length === 0
            ? <p className="px-1 py-6 text-center text-meta text-ink-2">Nothing yet. Searches, grabs and imports show up here.</p>
            : <Timeline groups={groupEvents(items.slice(0, PEEK))} onNavigate={onClose} />}
        </div>
        {items && items.length > 0 && (
          <Link href="/history/" onClick={onClose}
            className="block w-full border-t border-hair-2 px-4 py-3 text-center text-meta font-semibold text-ink-2 transition hover:bg-fill hover:text-ink">
            See all
          </Link>
        )}
      </div>
    </div>,
    document.body,
  );
}

// Every event, a page of twenty at a time; opening it reads everything.
export function NotificationHistory() {
  const { markRead } = useActivity();
  const { items, more, loadMore, error } = useActivityFeed({});
  const [page, setPage] = useState(0);
  useEffect(() => { void markRead(); }, [markRead]);

  const all = items ?? [];
  const shown = all.slice(page * NOTIFICATIONS_PER_PAGE, (page + 1) * NOTIFICATIONS_PER_PAGE);
  const hasOlder = all.length > (page + 1) * NOTIFICATIONS_PER_PAGE || more;
  const older = () => {
    // Fetch the next server page once this one is the last fully loaded.
    if (more && all.length < (page + 2) * NOTIFICATIONS_PER_PAGE) loadMore();
    setPage(page + 1);
  };

  return (
    <div>
      {error && <Alert>{error}</Alert>}
      {!items ? <TimelineSkeleton /> : items.length === 0 ? (
        <p className="px-1 py-8 text-center text-meta text-ink-2">Nothing has happened yet. Searches, grabs and imports show up here.</p>
      ) : (
        <>
          {shown.length > 0 ? <Timeline groups={groupEvents(shown)} stickyTop="top-[calc(64px+env(safe-area-inset-top))] md:top-[84px]" /> : <TimelineSkeleton />}
          {(page > 0 || hasOlder) && (
            <div className="flex items-center justify-between gap-2 pt-3">
              <Button size="sm" variant="ghost" icon="chevronLeft" disabled={page === 0} onClick={() => { setPage(page - 1); scrollTo({ top: 0 }); }}>Newer</Button>
              <span className="text-meta text-ink-3 tabular-nums">Page {page + 1}</span>
              <Button size="sm" variant="ghost" disabled={!hasOlder} onClick={() => { older(); scrollTo({ top: 0 }); }}>Older<Icon name="chevronRight" size={14} /></Button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

// What happened to one title: live download progress, then searches, grabs and imports.
export function ActivityTimeline({ scope }: { scope: Scope }) {
  const { progress } = useActivity();
  const { items, more, loadMore, error } = useActivityFeed(scope);
  const downloading = [...new Set((items ?? []).map((a) => a.downloadId))].flatMap((id) => (id === null ? [] : progress.get(id) ?? []));

  return (
    <section aria-labelledby="activity-heading">
      <h2 id="activity-heading" className="mb-4 text-title font-semibold">History</h2>
      {error && <Alert>{error}</Alert>}
      {!items ? <TimelineSkeleton rows={2} /> : items.length === 0 && downloading.length === 0 ? (
        <p className="well rounded-tile px-5 py-6 text-meta text-ink-2">Nothing has happened yet. Searches, grabs and imports for this title show up here.</p>
      ) : (
        <div className="space-y-4">
          {downloading.map((p) => (
            <div key={p.id} className="well rounded-tile p-4">
              <div className="mb-2.5 flex items-center justify-between gap-3 text-meta">
                <span className="flex items-center gap-2 font-semibold text-live"><span className="size-2 animate-pulse-dot rounded-full bg-live" />Downloading</span>
                <span className="flex gap-3 text-ink-2 tabular-nums">
                  <span>{formatSize(p.rate)}/s</span>
                  {p.eta !== null && <span>{formatEta(p.eta)} left</span>}
                  <span className="text-ink">{Math.round(p.progress * 100)}%</span>
                </span>
              </div>
              <Progress value={p.progress} eta={p.eta !== null ? formatEta(p.eta) : null} label="Download progress" />
            </div>
          ))}
          <Timeline groups={groupEvents(items)} showTitle={false} />
          {more && <LoadMore onMore={loadMore} label="Show older" />}
        </div>
      )}
    </section>
  );
}
