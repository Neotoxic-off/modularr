"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { api, currentWeek, episodeCode, errorMessage, isoDay, kindOf, titleHref } from "@/lib/api";
import type { AiringEpisode, Kind } from "@/lib/api";
import { useActivity } from "@/components/Activity";
import { useToast } from "@/components/Toasts";
import { Alert, Button, Chip, Empty, Modal, PageHeader, Skeleton, Switch } from "@/components/ui";

type Category = "all" | Exclude<Kind, "movie">;

// ✓ on disk, ○ upcoming, ! aired and missing: the mark and its words, never colour alone.
function stateOf(e: AiringEpisode, today: string): { mark: string; label: string; tone: string } {
  if (e.hasFile) return { mark: "✓", label: "On disk", tone: "text-ok" };
  if (e.airDate > today) return { mark: "○", label: "Upcoming", tone: "text-ink-3" };
  return e.monitored ? { mark: "!", label: "Missing", tone: "text-warn" } : { mark: "○", label: "Not monitored", tone: "text-ink-3" };
}

const categories: readonly { value: Category; label: string }[] = [
  { value: "all", label: "All" },
  { value: "series", label: "Series" },
  { value: "anime", label: "Anime" },
];

// shortcut: the week comes from the dashboard summary; prev/next weeks need a GET /calendar?start&end range endpoint.
export default function CalendarPage() {
  const { epoch } = useActivity();
  const [airing, setAiring] = useState<AiringEpisode[] | null>(null);
  const [kinds, setKinds] = useState<ReadonlyMap<number, Kind>>(new Map());
  const [category, setCategory] = useState<Category>("all");
  const [open, setOpen] = useState<AiringEpisode | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    api.dashboard().then((d) => setAiring(d.airing)).catch((e: unknown) => setError(errorMessage(e)));
    api.series().then((all) => setKinds(new Map(all.map((s) => [s.id, kindOf(s)])))).catch(() => undefined);
  }, []);
  useEffect(load, [load, epoch]);

  const today = isoDay(new Date());
  const week = currentWeek();
  const range = `${week[0]?.toLocaleDateString(undefined, { day: "numeric", month: "long" })} to ${week[6]?.toLocaleDateString(undefined, { day: "numeric", month: "long" })}`;
  const shown = (airing ?? []).filter((e) => category === "all" || kinds.get(e.seriesId) === category);

  return (
    <>
      <PageHeader title="Calendar" description={`This week, ${range}.`}>
        <div role="group" aria-label="Category" className="flex gap-2">
          {categories.map((c) => <Chip key={c.value} selected={category === c.value} onClick={() => setCategory(c.value)}>{c.label}</Chip>)}
        </div>
      </PageHeader>
      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      {airing && shown.length === 0 ? (
        <Empty icon="calendar" title="Nothing airing this week">
          New episodes of the series you follow show up here. <Link href="/explore/" className="font-semibold text-ink hover:underline">Explore</Link>
        </Empty>
      ) : (
        <ol className="grid gap-3 md:grid-cols-7">
          {week.map((d) => {
            const day = isoDay(d);
            const episodes = shown.filter((e) => e.airDate === day);
            return (
              <li key={day} aria-label={d.toLocaleDateString(undefined, { weekday: "long", day: "numeric", month: "long" })}
                className={`glass flex min-w-0 flex-col gap-2 rounded-tile p-3 md:min-h-[360px] ${day === today ? "ring-1 ring-white/40" : ""} ${airing && episodes.length === 0 ? "max-md:hidden" : ""}`}>
                <p className="flex items-baseline justify-between gap-2 px-1 pt-1">
                  <span className="caption">{d.toLocaleDateString(undefined, { weekday: "short" })}</span>
                  <span className={`font-display text-[28px] leading-none ${day === today ? "" : "text-ink-2"}`}>{d.getDate()}</span>
                </p>
                {!airing ? <Skeleton className="h-16 rounded-card" /> : episodes.length === 0 ? (
                  <p className="px-1 text-[13px] text-ink-3">Nothing airing.</p>
                ) : (
                  <ul className="space-y-2">
                    {episodes.map((e) => {
                      const state = stateOf(e, today);
                      return (
                        <li key={e.id}>
                          <button onClick={() => setOpen(e)} aria-label={`${e.seriesTitle} ${episodeCode(e)}, ${state.label}`}
                            className="well flex w-full cursor-pointer items-center gap-2.5 rounded-card p-2 text-left transition hover:bg-fill md:flex-col md:items-stretch">
                            {e.poster && (
                              // eslint-disable-next-line @next/next/no-img-element
                              <img src={e.poster} alt="" loading="lazy" className="h-14 w-10 shrink-0 rounded-[8px] object-cover md:aspect-[2/3] md:h-auto md:w-full" />
                            )}
                            <span className="min-w-0 flex-1">
                              <span className="block truncate text-[13px] font-semibold">{e.seriesTitle}</span>
                              <span className="flex items-center gap-1.5 text-[12px] text-ink-3 tabular-nums">
                                {episodeCode(e)}
                                <span className={`font-bold ${state.tone}`} aria-hidden="true">{state.mark}</span>
                              </span>
                            </span>
                          </button>
                        </li>
                      );
                    })}
                  </ul>
                )}
              </li>
            );
          })}
        </ol>
      )}
      {open && <EpisodeSheet episode={open} today={today} onClose={() => setOpen(null)}
        onChange={(e) => { setOpen(e); setAiring((all) => all && all.map((x) => (x.id === e.id ? e : x))); }} />}
    </>
  );
}

function EpisodeSheet({ episode: e, today, onClose, onChange }: { episode: AiringEpisode; today: string; onClose: () => void; onChange: (e: AiringEpisode) => void }) {
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  const state = stateOf(e, today);
  const fail = (error: unknown) => toast({ key: `series:${e.seriesId}`, icon: "alert", tone: "bad", title: e.seriesTitle, message: errorMessage(error) });

  async function search() {
    setBusy(true);
    try {
      await api.autoSearch("series", e.seriesId, e.seasonNumber);
      toast({ key: `series:${e.seriesId}`, icon: "search", tone: "seek", title: e.seriesTitle, message: `Searching season ${e.seasonNumber}.` });
      onClose();
    } catch (error) {
      fail(error);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal onClose={onClose} title={`${e.seriesTitle} ${episodeCode(e)}`}
      description={`${e.title ?? "Untitled episode"} · ${new Date(`${e.airDate}T00:00`).toLocaleDateString(undefined, { weekday: "long", day: "numeric", month: "long" })}`}
      footer={<>
        {!e.hasFile && e.airDate <= today && <Button icon="bolt" loading={busy} onClick={search}>Search season {e.seasonNumber}</Button>}
        <Link href={`${titleHref("series", e.seriesId)}&season=${e.seasonNumber}`}
          className="inline-flex h-11 items-center rounded-pill bg-white px-5 text-sm font-semibold text-canvas hover:bg-white/85">Open title</Link>
      </>}>
      <div className="space-y-3 px-5 pb-5">
        <p className={`text-sm font-semibold ${state.tone}`}><span aria-hidden="true">{state.mark} </span>{state.label}</p>
        <Switch checked={e.monitored} label="Monitored" description="Monitored episodes are grabbed as soon as they air."
          onChange={(monitored) => void api.setEpisodeMonitored(e.id, monitored).then((x) => onChange({ ...e, monitored: x.monitored }), fail)} />
      </div>
    </Modal>
  );
}
