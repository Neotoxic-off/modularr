"use client";

import Link from "next/link";
import { formatAgo, formatEta, formatSize } from "@/lib/api";
import type { Activity } from "@/lib/api";
import { activityHref, kindIcon, levelTone, useActivity, useActivityFeed } from "@/components/Activity";
import type { Scope } from "@/components/Activity";
import { Icon } from "@/components/icons";
import { Alert, Button, Empty, Modal, Skeleton, tones } from "@/components/ui";

function ActivityIcon({ activity, className = "" }: { activity: Activity; className?: string }) {
  return (
    <span className={`grid size-8 shrink-0 place-items-center rounded-lg ring-1 ring-inset ${tones[levelTone[activity.level]]} ${className}`}>
      <Icon name={kindIcon[activity.kind]} size={15} />
    </span>
  );
}

function Time({ iso }: { iso: string }) {
  return <time dateTime={iso} title={new Date(iso).toLocaleString()} className="shrink-0 text-xs text-muted tabular-nums">{formatAgo(iso, "short")}</time>;
}

const dayStart = (offset: number) => {
  const d = new Date();
  d.setHours(0, 0, 0, 0);
  return d.getTime() - offset * 86400000;
};

function byDay(items: Activity[]): [string, Activity[]][] {
  const [today, yesterday] = [dayStart(0), dayStart(1)];
  const groups: [string, Activity[]][] = [["Today", []], ["Yesterday", []], ["Earlier", []]];
  for (const a of items) {
    const time = new Date(a.time).getTime();
    groups[time >= today ? 0 : time >= yesterday ? 1 : 2]?.[1].push(a);
  }
  return groups.filter(([, list]) => list.length > 0);
}

function FeedSkeleton() {
  return <div className="space-y-3 p-5">{Array.from({ length: 5 }, (_, i) => <Skeleton key={i} className="h-14" />)}</div>;
}

export function NotificationPanel({ onClose }: { onClose: () => void }) {
  const { unread, markRead } = useActivity();
  const { items, setItems, more, loadMore, error } = useActivityFeed({});

  async function readAll() {
    const newest = items?.[0];
    if (!newest) return;
    await markRead(newest.id);
    setItems((list) => list && list.map((a) => (a.id <= newest.id ? { ...a, read: true } : a)));
  }

  return (
    <Modal size="side" onClose={onClose} title="Notifications"
      description={unread > 0 ? `${unread} unread` : "You’re all caught up"}
      actions={unread > 0 && !!items?.length && <Button variant="ghost" size="sm" onClick={readAll}><Icon name="check" size={14} />Mark all read</Button>}>
      {error && <div className="p-5"><Alert>{error}</Alert></div>}
      {!items ? <FeedSkeleton /> : items.length === 0 ? (
        <div className="p-5">
          <Empty icon="bell" title="No notifications yet">Searches, grabs and imports show up here as they happen.</Empty>
        </div>
      ) : (
        <div className="pb-4">
          {byDay(items).map(([day, list]) => (
            <section key={day} aria-label={day}>
              <h3 className="sticky top-0 z-10 bg-surface/95 px-5 pt-4 pb-2 text-xs font-medium text-muted backdrop-blur">{day}</h3>
              <ul className="px-2">
                {list.map((a) => <PanelItem key={a.id} activity={a} onOpen={onClose} />)}
              </ul>
            </section>
          ))}
          {more && <div className="px-5 pt-2"><Button variant="ghost" size="sm" className="w-full" onClick={loadMore}>Show older</Button></div>}
        </div>
      )}
    </Modal>
  );
}

function PanelItem({ activity: a, onOpen }: { activity: Activity; onOpen: () => void }) {
  const href = activityHref(a);
  const body = (
    <>
      <ActivityIcon activity={a} />
      <span className="min-w-0 flex-1">
        <span className="flex items-baseline gap-2">
          <span className={`min-w-0 flex-1 truncate text-sm ${a.read ? "text-foreground/80" : "font-medium"}`}>{a.title}</span>
          <Time iso={a.time} />
        </span>
        {a.message && <span className="mt-0.5 line-clamp-2 block text-xs leading-relaxed text-muted">{a.message}</span>}
      </span>
      <span aria-label={a.read ? undefined : "Unread"} className={`mt-1.5 size-1.5 shrink-0 rounded-full ${a.read ? "" : "bg-accent"}`} />
    </>
  );
  const row = "flex items-start gap-3 rounded-xl px-3 py-2.5 transition";
  return (
    <li>
      {href
        ? <Link href={href} onClick={onOpen} className={`${row} hover:bg-raised/70`}>{body}</Link>
        : <div className={row}>{body}</div>}
    </li>
  );
}

// What happened to one title: searches, grabs, live download progress and imports.
export function ActivityTimeline({ scope, itemTitle }: { scope: Scope; itemTitle: string }) {
  const { progress } = useActivity();
  const { items, more, loadMore, error } = useActivityFeed(scope);
  const downloading = [...new Set((items ?? []).map((a) => a.downloadId))].flatMap((id) => (id === null ? [] : progress.get(id) ?? []));

  return (
    <section aria-labelledby="activity-heading">
      <h2 id="activity-heading" className="mb-4 font-display text-xl font-semibold">Activity</h2>
      {error && <Alert>{error}</Alert>}
      {!items ? <Skeleton className="h-32" /> : items.length === 0 && downloading.length === 0 ? (
        <p className="rounded-2xl border border-dashed border-line px-5 py-6 text-sm text-muted">Nothing has happened yet. Searches, grabs and imports for this title show up here.</p>
      ) : (
        <div className="rounded-2xl border border-line bg-surface px-5 py-4">
          {downloading.map((p) => (
            <div key={p.id} className="mb-4 rounded-xl bg-accent/[0.07] p-3.5 ring-1 ring-accent/15">
              <div className="flex items-center justify-between gap-3 text-sm">
                <span className="flex items-center gap-2 font-medium"><span className="size-2 animate-pulse rounded-full bg-accent" />Downloading</span>
                <span className="flex gap-3 text-xs text-muted tabular-nums">
                  <span>{formatSize(p.rate)}/s</span>
                  {p.eta !== null && <span>{formatEta(p.eta)} left</span>}
                  <span className="text-foreground">{Math.round(p.progress * 100)}%</span>
                </span>
              </div>
              <div className="mt-2.5 h-1.5 overflow-hidden rounded-full bg-line" role="progressbar" aria-label="Download progress"
                aria-valuenow={Math.round(p.progress * 100)} aria-valuemin={0} aria-valuemax={100}>
                <div className="h-full rounded-full bg-gradient-to-r from-accent-strong to-accent transition-[width] duration-700" style={{ width: `${p.progress * 100}%` }} />
              </div>
            </div>
          ))}
          <ol>
            {items.map((a, i) => (
              <li key={a.id} className="relative flex gap-3.5 pb-5 last:pb-0">
                {i < items.length - 1 && <span aria-hidden="true" className="absolute top-9 bottom-1 left-4 w-px bg-line" />}
                <ActivityIcon activity={a} />
                <div className="min-w-0 flex-1 pt-1">
                  <div className="flex items-baseline gap-3">
                    <p className="min-w-0 flex-1 text-sm">{a.message ?? a.kind}</p>
                    <Time iso={a.time} />
                  </div>
                  {a.title !== itemTitle && <p className="mt-0.5 truncate text-xs text-muted" title={a.title}>{a.title}</p>}
                </div>
              </li>
            ))}
          </ol>
          {more && <Button variant="ghost" size="sm" className="mt-3 w-full" onClick={loadMore}>Show older</Button>}
        </div>
      )}
    </section>
  );
}
