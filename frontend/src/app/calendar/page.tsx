"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { api, currentWeek, episodeCode, errorMessage, isoDay, titleHref } from "@/lib/api";
import type { AiringEpisode } from "@/lib/api";
import { useActivity } from "@/components/Activity";
import { Alert, PageHeader, Skeleton } from "@/components/ui";

// ✓ on disk, ○ upcoming, ! aired and missing: the mark and its words, never colour alone.
function stateOf(e: AiringEpisode, today: string): { mark: string; label: string; tone: string } {
  if (e.hasFile) return { mark: "✓", label: "On disk", tone: "text-ok" };
  if (e.airDate > today) return { mark: "○", label: "Upcoming", tone: "text-ink-3" };
  return e.monitored ? { mark: "!", label: "Missing", tone: "text-warn" } : { mark: "○", label: "Not monitored", tone: "text-ink-3" };
}

// shortcut: the week comes from the dashboard summary; prev/next weeks need a GET /calendar?start&end range endpoint.
export default function CalendarPage() {
  const { epoch } = useActivity();
  const [airing, setAiring] = useState<AiringEpisode[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    api.dashboard().then((d) => setAiring(d.airing)).catch((e: unknown) => setError(errorMessage(e)));
  }, []);
  useEffect(load, [load, epoch]);

  const today = isoDay(new Date());
  const week = currentWeek();
  const range = `${week[0]?.toLocaleDateString(undefined, { day: "numeric", month: "long" })} to ${week[6]?.toLocaleDateString(undefined, { day: "numeric", month: "long" })}`;

  return (
    <>
      <PageHeader title="Calendar" description={`This week, ${range}.`} />
      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      <ol className="grid gap-3 md:grid-cols-7">
        {week.map((d) => {
          const day = isoDay(d);
          const episodes = (airing ?? []).filter((e) => e.airDate === day);
          return (
            <li key={day} aria-label={d.toLocaleDateString(undefined, { weekday: "long", day: "numeric", month: "long" })}
              className={`glass flex min-w-0 flex-col gap-2 rounded-tile p-3 md:min-h-[360px] ${day === today ? "ring-1 ring-white/40" : ""} ${airing && episodes.length === 0 ? "max-md:hidden" : ""}`}>
              <p className="flex items-baseline justify-between gap-2 px-1 pt-1">
                <span className="caption">{d.toLocaleDateString(undefined, { weekday: "short" })}</span>
                <span className={`font-serif text-[28px] leading-none ${day === today ? "" : "text-ink-2"}`}>{d.getDate()}</span>
              </p>
              {!airing ? <Skeleton className="h-16 rounded-card" /> : episodes.length === 0 ? (
                <p className="px-1 text-[13px] text-ink-3">Nothing airing.</p>
              ) : (
                <ul className="space-y-2">
                  {episodes.map((e) => {
                    const state = stateOf(e, today);
                    return (
                      <li key={e.id}>
                        <Link href={`${titleHref("series", e.seriesId)}&season=${e.seasonNumber}`}
                          className="well flex items-center gap-2.5 rounded-card p-2 transition hover:bg-fill md:flex-col md:items-stretch">
                          {e.poster && (
                            // eslint-disable-next-line @next/next/no-img-element
                            <img src={e.poster} alt="" loading="lazy" className="h-14 w-10 shrink-0 rounded-[8px] object-cover md:h-auto md:w-full md:aspect-[2/3]" />
                          )}
                          <span className="min-w-0 flex-1">
                            <span className="block truncate text-[13px] font-semibold">{e.seriesTitle}</span>
                            <span className="flex items-center gap-1.5 text-[12px] text-ink-3 tabular-nums">
                              {episodeCode(e)}
                              <span className={`font-bold ${state.tone}`} aria-hidden="true">{state.mark}</span>
                              <span className="sr-only">{state.label}</span>
                            </span>
                          </span>
                        </Link>
                      </li>
                    );
                  })}
                </ul>
              )}
            </li>
          );
        })}
      </ol>
      {airing?.length === 0 && <p className="mt-4 text-center text-[13px] text-ink-2 md:hidden">Nothing airing this week.</p>}
    </>
  );
}
