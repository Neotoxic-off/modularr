"use client";

import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState, useSyncExternalStore } from "react";
import { api, episodeCode, errorMessage, isoDay, titleHref } from "@/lib/api";
import type { CalendarEvent, Kind } from "@/lib/api";
import { useActivity } from "@/components/Activity";
import { Icon } from "@/components/icons";
import { Alert, Badge, Button, Chip, Empty, IconButton, PageHeader, Segmented, Skeleton, toneDot } from "@/components/ui";
import type { Tone } from "@/components/ui";

type View = "month" | "week" | "agenda";
type Filter = "all" | Kind;

const views: readonly { value: View; label: string }[] = [
  { value: "month", label: "Month" },
  { value: "week", label: "Week" },
  { value: "agenda", label: "Agenda" },
];

const filters: readonly { value: Filter; label: string }[] = [
  { value: "all", label: "All" },
  { value: "movie", label: "Movies" },
  { value: "series", label: "Series" },
  { value: "anime", label: "Anime" },
];

const releases = { cinema: "In cinemas", digital: "Digital release", physical: "Physical release" } as const;

const isView = (v: string | null): v is View => views.some((o) => o.value === v);
const isFilter = (v: string | null): v is Filter => filters.some((o) => o.value === v);

const addDays = (d: Date, n: number) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + n);
const mondayOf = (d: Date) => addDays(d, -((d.getDay() + 6) % 7));

function parseDay(value: string | null): Date {
  const [y, m, d] = (value ?? "").split("-").map(Number);
  const date = new Date(y ?? NaN, (m ?? NaN) - 1, d ?? NaN);
  if (!Number.isNaN(date.getTime())) return date;
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

// Month and agenda cover the anchor's month (month pads it to whole weeks), week its Monday to Sunday.
function rangeOf(view: View, anchor: Date): { start: Date; end: Date; label: string } {
  if (view === "week") {
    const start = mondayOf(anchor);
    const end = addDays(start, 6);
    return { start, end, label: new Intl.DateTimeFormat(undefined, { day: "numeric", month: "long", year: "numeric" }).formatRange(start, end) };
  }
  const first = new Date(anchor.getFullYear(), anchor.getMonth(), 1);
  const last = new Date(anchor.getFullYear(), anchor.getMonth() + 1, 0);
  const label = first.toLocaleDateString(undefined, { month: "long", year: "numeric" });
  return view === "month" ? { start: mondayOf(first), end: addDays(mondayOf(last), 6), label } : { start: first, end: last, label };
}

function stateOf(e: CalendarEvent, today: string): { tone: Tone; label: string } {
  if (e.hasFile) return { tone: "ok", label: "Downloaded" };
  return e.date > today ? { tone: "neutral", label: "Upcoming" } : { tone: "warn", label: "Missing" };
}

const detailOf = (e: CalendarEvent) =>
  e.type === "movie" ? releases[e.release] : `${episodeCode({ seasonNumber: e.season, episodeNumber: e.episode })}${e.episodeTitle ? ` · ${e.episodeTitle}` : ""}`;

const hrefOf = (e: CalendarEvent) => (e.type === "movie" ? titleHref("movie", e.movieId) : `${titleHref("series", e.seriesId)}&season=${e.season}`);

const keyOf = (e: CalendarEvent) => (e.type === "movie" ? `m${e.movieId}${e.release}` : `e${e.seriesId}-${e.season}-${e.episode}`);

const longDay = (day: string) => parseDay(day).toLocaleDateString(undefined, { weekday: "long", day: "numeric", month: "long" });

const phoneQuery = "(max-width: 767px)";
const subscribePhone = (onChange: () => void) => {
  const media = window.matchMedia(phoneQuery);
  media.addEventListener("change", onChange);
  return () => media.removeEventListener("change", onChange);
};

export default function CalendarPage() {
  return <Suspense><Calendar /></Suspense>;
}

function Calendar() {
  const params = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const { epoch } = useActivity();
  const phone = useSyncExternalStore(subscribePhone, () => window.matchMedia(phoneQuery).matches, () => false);
  const [loaded, setLoaded] = useState<{ key: string; events: CalendarEvent[] } | null>(null);
  const [upcoming, setUpcoming] = useState<{ epoch: number; events: CalendarEvent[] } | null>(null);
  const [error, setError] = useState<string | null>(null);

  const viewParam = params.get("view");
  const filterParam = params.get("show");
  const view: View = isView(viewParam) ? viewParam : phone ? "agenda" : "month";
  const filter: Filter = isFilter(filterParam) ? filterParam : "all";
  const missingOnly = params.get("missing") === "1";
  const anchor = parseDay(params.get("date"));
  const range = rangeOf(view, anchor);
  const start = isoDay(range.start);
  const end = isoDay(range.end);
  const key = `${start}/${end}/${epoch}`;
  const today = isoDay(new Date());

  const set = (changes: Record<string, string>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(changes)) {
      if (v) next.set(k, v); else next.delete(k);
    }
    const query = next.toString();
    router.replace(query ? `${pathname}?${query}` : pathname, { scroll: false });
  };
  const step = (direction: number) =>
    set({ date: isoDay(view === "week" ? addDays(anchor, 7 * direction) : new Date(anchor.getFullYear(), anchor.getMonth() + direction, 1)) });

  useEffect(() => {
    api.calendar(start, end).then((events) => { setLoaded({ key, events }); setError(null); }, (e: unknown) => setError(errorMessage(e)));
  }, [start, end, key]);

  const matches = (e: CalendarEvent) =>
    (filter === "all" || (filter === "movie" ? e.type === "movie" : e.type === "episode" && e.kind === filter)) && (!missingOnly || !e.hasFile);
  const events = loaded?.key === key ? loaded.events.filter(matches) : null;
  const needUpNext = events?.length === 0;

  useEffect(() => {
    if (needUpNext && upcoming?.epoch !== epoch) {
      api.upcoming().then((all) => setUpcoming({ epoch, events: all }), (e: unknown) => setError(errorMessage(e)));
    }
  }, [needUpNext, upcoming, epoch]);
  const upNext = upcoming?.events.filter(matches);

  return (
    <>
      <PageHeader title="Calendar" description="Episodes airing and movies releasing from your library." />
      <div className="mb-5 flex flex-wrap items-center gap-x-4 gap-y-3">
        <div className="flex items-center gap-2">
          <Button size="sm" onClick={() => set({ date: "" })}>Today</Button>
          <IconButton icon="chevronLeft" label={`Previous ${view === "week" ? "week" : "month"}`} onClick={() => step(-1)} className="size-9 pointer-coarse:size-11" />
          <IconButton icon="chevronRight" label={`Next ${view === "week" ? "week" : "month"}`} onClick={() => step(1)} className="size-9 pointer-coarse:size-11" />
        </div>
        <h2 aria-live="polite" className="min-w-0 flex-1 text-[20px] font-semibold tracking-tight">{range.label}</h2>
        <Segmented label="View" value={view} options={views} onChange={(v) => set({ view: v })} />
      </div>
      <div role="group" aria-label="Filters" className="rail -mx-4 mb-5 flex gap-2 overflow-x-auto px-4 md:mx-0 md:flex-wrap md:px-0">
        {filters.map((f) => <Chip key={f.value} selected={filter === f.value} onClick={() => set({ show: f.value === "all" ? "" : f.value })}>{f.label}</Chip>)}
        <span aria-hidden="true" className="mx-1 w-px shrink-0 self-stretch bg-hair" />
        <Chip selected={missingOnly} onClick={() => set({ missing: missingOnly ? "" : "1" })}>Only missing</Chip>
      </div>
      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      {!events ? <Skeleton className="h-[480px] rounded-tile" />
        : events.length > 0 ? (
          view === "month" ? <MonthGrid events={events} range={range} month={anchor.getMonth()} today={today} onDay={(day) => set({ view: "week", date: day })} />
          : view === "week" ? <WeekGrid events={events} start={range.start} today={today} />
          : <Agenda events={events} today={today} />
        )
        : !upNext ? <Skeleton className="h-[240px] rounded-tile" />
        : upNext.length > 0 ? (
          <section aria-labelledby="up-next" className="space-y-4">
            <div>
              <h2 id="up-next" className="text-[17px] font-semibold">Up next</h2>
              <p className="mt-0.5 text-[13px] text-ink-3">Nothing {missingOnly || filter !== "all" ? "matches" : "lands"} in {range.label}. These are the next dates in your library.</p>
            </div>
            <Agenda events={upNext} today={today} />
          </section>
        ) : (
          <Empty icon="calendar" title={upcoming?.events.length ? "Nothing coming up matches these filters" : "Nothing on the calendar"}>
            Episodes and movie releases from your library show up here. <Link href="/explore/" className="font-semibold text-ink hover:underline">Explore</Link>
          </Empty>
        )}
    </>
  );
}

function groupByDay(events: CalendarEvent[]): Map<string, CalendarEvent[]> {
  const days = new Map<string, CalendarEvent[]>();
  for (const e of events) days.set(e.date, [...(days.get(e.date) ?? []), e]);
  return days;
}

function MonthGrid({ events, range, month, today, onDay }: {
  events: CalendarEvent[];
  range: { start: Date; end: Date };
  month: number;
  today: string;
  onDay: (day: string) => void;
}) {
  const byDay = groupByDay(events);
  const days: Date[] = [];
  for (let d = range.start; d <= range.end; d = addDays(d, 1)) days.push(d);
  return (
    <div className="glass overflow-hidden rounded-tile">
      <div aria-hidden="true" className="grid grid-cols-7 border-b border-hair-2">
        {days.slice(0, 7).map((d) => <span key={d.getDay()} className="caption px-2 py-2.5 text-center sm:px-3 sm:text-left">{d.toLocaleDateString(undefined, { weekday: "short" })}</span>)}
      </div>
      <ol className="grid grid-cols-7">
        {days.map((d) => {
          const day = isoDay(d);
          const onDate = byDay.get(day) ?? [];
          const outside = d.getMonth() !== month;
          return (
            <li key={day} aria-label={`${longDay(day)}, ${onDate.length} ${onDate.length === 1 ? "event" : "events"}`}
              className={`flex min-h-16 min-w-0 flex-col gap-1 border-hair-2 p-1 not-nth-[7n]:border-r not-nth-last-[-n+7]:border-b sm:min-h-32 sm:p-1.5 ${outside ? "bg-black/50" : ""}`}>
              <button type="button" onClick={() => onDay(day)} aria-label={`Show the week of ${longDay(day)}`}
                className={`grid size-9 cursor-pointer place-items-center self-center rounded-full sm:size-7 text-[12px] font-semibold tabular-nums transition hover:bg-fill-2 sm:self-start ${day === today ? "bg-white text-canvas hover:bg-white/85" : outside ? "text-ink-3" : "text-ink"}`}>
                {d.getDate()}
              </button>
              <ul className="hidden min-w-0 space-y-1 sm:block">
                {onDate.slice(0, 3).map((e) => <li key={keyOf(e)}><EventLink event={e} today={today} compact /></li>)}
              </ul>
              {onDate.length > 3 && (
                <button type="button" onClick={() => onDay(day)} className="hidden cursor-pointer rounded-chip px-1.5 text-left text-[11px] font-semibold text-ink-3 hover:text-ink sm:block">
                  {onDate.length - 3} more
                </button>
              )}
              {onDate.length > 0 && (
                <span aria-hidden="true" className="flex flex-wrap justify-center gap-1 sm:hidden">
                  {onDate.slice(0, 4).map((e) => <span key={keyOf(e)} className={`size-1.5 rounded-full ${toneDot[stateOf(e, today).tone]}`} />)}
                </span>
              )}
            </li>
          );
        })}
      </ol>
    </div>
  );
}

function WeekGrid({ events, start, today }: { events: CalendarEvent[]; start: Date; today: string }) {
  const byDay = groupByDay(events);
  return (
    <ol className="grid gap-3 md:grid-cols-7">
      {Array.from({ length: 7 }, (_, i) => isoDay(addDays(start, i))).map((day) => {
        const onDate = byDay.get(day) ?? [];
        return (
          <li key={day} aria-label={longDay(day)}
            className={`glass flex min-w-0 flex-col gap-2 rounded-tile p-2.5 md:min-h-[420px] ${day === today ? "ring-1 ring-white/40" : ""} ${onDate.length === 0 ? "max-md:hidden" : ""}`}>
            <p className="flex items-baseline justify-between gap-2 px-1 pt-1">
              <span className="caption">{parseDay(day).toLocaleDateString(undefined, { weekday: "short" })}</span>
              <span className={`font-display text-[24px] leading-none tabular-nums ${day === today ? "" : "text-ink-2"}`}>{parseDay(day).getDate()}</span>
            </p>
            {onDate.length === 0 ? <p className="px-1 text-[12px] text-ink-3">Nothing</p> : (
              <ul className="space-y-1.5">
                {onDate.map((e) => <li key={keyOf(e)}><EventLink event={e} today={today} stacked /></li>)}
              </ul>
            )}
          </li>
        );
      })}
    </ol>
  );
}

function Agenda({ events, today }: { events: CalendarEvent[]; today: string }) {
  return (
    <ol className="space-y-5">
      {[...groupByDay(events)].map(([day, onDate]) => (
        <li key={day} className="grid gap-2 md:grid-cols-[180px_1fr] md:gap-5">
          <h3 className={`pt-2 text-[13px] font-semibold ${day === today ? "text-ink" : "text-ink-2"}`}>
            {day === today ? "Today · " : ""}{longDay(day)}
          </h3>
          <ul className="glass divide-y divide-hair-2 overflow-hidden rounded-tile">
            {onDate.map((e) => <li key={keyOf(e)}><EventLink event={e} today={today} /></li>)}
          </ul>
        </li>
      ))}
    </ol>
  );
}

// compact: a month cell chip; stacked: a narrow week column card; otherwise a full agenda row.
function EventLink({ event: e, today, compact = false, stacked = false }: { event: CalendarEvent; today: string; compact?: boolean; stacked?: boolean }) {
  const state = stateOf(e, today);
  const label = `${e.title}, ${detailOf(e)}, ${state.label}`;
  const poster = (className: string) => e.poster
    // eslint-disable-next-line @next/next/no-img-element
    ? <img src={e.poster} alt="" loading="lazy" className={`shrink-0 rounded-[6px] bg-fill-2 object-cover ${className}`} />
    : <span className={`grid shrink-0 place-items-center rounded-[6px] bg-fill-2 text-ink-3 ${className}`}><Icon name={e.type === "movie" ? "film" : "tv"} size={14} /></span>;

  if (compact) {
    return (
      <Link href={hrefOf(e)} aria-label={label} title={label}
        className="flex min-w-0 items-center gap-1.5 rounded-chip p-1 text-[12px] transition hover:bg-fill-2">
        {poster("h-7 w-5")}
        <span className="min-w-0 flex-1 truncate font-medium">{e.title}</span>
        <span className={`size-1.5 shrink-0 rounded-full ${toneDot[state.tone]}`} />
      </Link>
    );
  }

  return (
    <Link href={hrefOf(e)} aria-label={label}
      className={`flex min-w-0 items-center gap-3 transition hover:bg-fill-2 ${stacked ? "well rounded-card p-2 md:flex-col md:items-stretch md:gap-2" : "px-4 py-3"}`}>
      {poster(stacked ? "h-14 w-10 md:aspect-[2/3] md:h-auto md:w-full" : "h-[60px] w-10")}
      <span className="min-w-0 flex-1">
        <span className="block truncate text-[13px] font-semibold">{e.title}</span>
        <span className={`block text-[12px] text-ink-3 truncate ${stacked ? "md:line-clamp-2 md:whitespace-normal" : ""}`}>{detailOf(e)}</span>
      </span>
      <Badge tone={state.tone} className={stacked ? "self-start" : ""}>
        {state.tone !== "neutral" && <span className={`size-1.5 rounded-full ${toneDot[state.tone]}`} />}{state.label}
      </Badge>
    </Link>
  );
}
