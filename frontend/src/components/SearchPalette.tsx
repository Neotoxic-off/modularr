"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import type { KeyboardEvent } from "react";
import { api, errorMessage, kindOfDiscovery, titleHref } from "@/lib/api";
import type { DiscoveryItem } from "@/lib/api";
import { useAddDialog } from "@/components/AddDialog";
import { Icon } from "@/components/icons";
import { kindLabel } from "@/components/Poster";
import { Badge, Rating, Spinner } from "@/components/ui";

const DEBOUNCE_MS = 250;

// Search-as-you-type over movies, series and anime; Enter opens the title or the add dialog.
export function SearchPalette({ onClose }: { onClose: () => void }) {
  const router = useRouter();
  const openAdd = useAddDialog();
  const input = useRef<HTMLInputElement>(null);
  const [term, setTerm] = useState("");
  const [results, setResults] = useState<DiscoveryItem[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [active, setActive] = useState(0);

  const query = term.trim();

  useEffect(() => {
    input.current?.focus();
    document.body.style.overflow = "hidden";
    return () => { document.body.style.overflow = ""; };
  }, []);

  useEffect(() => {
    if (query.length < 2) return;
    const abort = new AbortController();
    const timer = setTimeout(() => {
      setLoading(true);
      api.lookup(query, abort.signal)
        .then((r) => { setResults(r); setError(null); setActive(0); })
        .catch((e: unknown) => { if (!abort.signal.aborted) setError(errorMessage(e)); })
        .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    }, DEBOUNCE_MS);
    return () => { clearTimeout(timer); abort.abort(); };
  }, [query]);

  function choose(item: DiscoveryItem) {
    if (item.inLibrary && item.libraryId !== null) {
      onClose();
      router.push(titleHref(kindOfDiscovery(item), item.libraryId));
    } else {
      // The dialog moves on to the new title page, so the palette closes behind it.
      openAdd(item, onClose);
    }
  }

  const shown = query.length < 2 ? null : results;

  function key(e: KeyboardEvent) {
    if (e.key === "Escape") onClose();
    if (!shown?.length) return;
    if (e.key === "ArrowDown") { e.preventDefault(); setActive((a) => (a + 1) % shown.length); }
    if (e.key === "ArrowUp") { e.preventDefault(); setActive((a) => (a - 1 + shown.length) % shown.length); }
    if (e.key === "Enter" && shown[active]) { e.preventDefault(); choose(shown[active]); }
  }

  return (
    <div className="fixed inset-0 z-40 flex animate-fade justify-center bg-black/70 px-3 pt-[8vh] backdrop-blur-sm sm:pt-[12vh]" onMouseDown={onClose}>
      <div role="dialog" aria-modal="true" aria-label="Search" onMouseDown={(e) => e.stopPropagation()} onKeyDown={key}
        className="flex h-fit max-h-[78vh] w-full max-w-2xl animate-rise flex-col overflow-hidden rounded-2xl border border-line bg-surface shadow-2xl shadow-black/70">
        <div className="flex items-center gap-3 border-b border-line px-4">
          {loading ? <Spinner className="text-accent" /> : <Icon name="search" className="text-muted" />}
          <input ref={input} value={term} onChange={(e) => setTerm(e.target.value)} role="combobox" aria-expanded={!!shown?.length}
            aria-controls="search-results" aria-activedescendant={shown?.length ? `result-${active}` : undefined}
            placeholder="Search movies, series and anime" aria-label="Search titles"
            className="h-14 min-w-0 flex-1 bg-transparent text-base outline-none placeholder:text-muted/70 focus-visible:outline-none" />
          <kbd className="hidden rounded-md border border-line px-1.5 py-0.5 text-[11px] text-muted sm:block">Esc</kbd>
        </div>

        <div className="min-h-0 overflow-y-auto">
          {query.length < 2 ? (
            <div className="px-5 py-8 text-sm text-muted">
              <p>Type a title, optionally followed by a year: <span className="text-foreground">dune 2021</span>.</p>
              <p className="mt-1.5">Ids work too: <span className="text-foreground">tmdb:438631</span>, <span className="text-foreground">tvdb:81189</span>, <span className="text-foreground">tt1160419</span>.</p>
            </div>
          ) : error ? (
            <p className="px-5 py-8 text-sm text-red-300">Search is unavailable right now: {error}</p>
          ) : shown === null ? (
            <p className="px-5 py-8 text-sm text-muted">Searching…</p>
          ) : shown.length === 0 ? (
            <p className="px-5 py-8 text-sm text-muted">Nothing matches “{query}”. Try another spelling or add the year.</p>
          ) : (
            <ul id="search-results" role="listbox" className="p-2">
              {shown.map((item, i) => (
                <li key={`${item.kind}${item.tmdbId}${item.tvdbId}`} id={`result-${i}`} role="option" aria-selected={i === active}
                  onMouseMove={() => setActive(i)} onClick={() => choose(item)}
                  className="flex cursor-pointer items-center gap-3 rounded-xl p-2 aria-selected:bg-raised">
                  <span className="h-16 w-11 shrink-0 overflow-hidden rounded-md bg-raised">
                    {item.poster && (
                      // eslint-disable-next-line @next/next/no-img-element
                      <img src={item.poster} alt="" loading="lazy" className="h-full w-full object-cover" />
                    )}
                  </span>
                  <div className="min-w-0 flex-1">
                    <p className="truncate font-medium">{item.title}</p>
                    <div className="mt-0.5 flex items-center gap-2.5 text-xs text-muted">
                      <Badge tone={item.kind === "Anime" ? "accent" : "neutral"}>{kindLabel[item.kind]}</Badge>
                      {item.year && <span>{item.year}</span>}
                      <Rating value={item.rating} />
                    </div>
                  </div>
                  {item.inLibrary
                    ? <Badge tone="green"><Icon name="check" size={12} />In library</Badge>
                    : <span className="flex items-center gap-1 text-xs text-muted"><Icon name="plus" size={14} />Add</span>}
                </li>
              ))}
            </ul>
          )}
        </div>
        <div className="hidden items-center gap-4 border-t border-line px-4 py-2 text-[11px] text-muted sm:flex">
          <span><kbd className="font-sans">↑ ↓</kbd> to move</span>
          <span><kbd className="font-sans">Enter</kbd> to open or add</span>
        </div>
      </div>
    </div>
  );
}
