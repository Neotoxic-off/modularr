"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { api, errorMessage, titleHref } from "@/lib/api";
import type { DiscoveryItem, ImportOutcome, ImportProposal, ImportProposals, LibraryRoot, MatchConfidence } from "@/lib/api";
import { Icon } from "@/components/icons";
import { PosterArt } from "@/components/Poster";
import { Alert, Badge, Button, Empty, Input, Modal, PageHeader, Skeleton, Spinner, Segmented } from "@/components/ui";
import type { Tone } from "@/components/ui";

const roots: readonly { value: LibraryRoot; label: string }[] = [
  { value: "movies", label: "Movies" },
  { value: "series", label: "Series" },
  { value: "anime", label: "Anime" },
];

const confidence: Record<MatchConfidence, { tone: Tone; label: string }> = {
  High: { tone: "ok", label: "Sure match" },
  Medium: { tone: "live", label: "Likely match" },
  Low: { tone: "warn", label: "Unsure match" },
  None: { tone: "neutral", label: "No match" },
};

const folderName = (path: string) => path.split(/[\\/]/).filter(Boolean).pop() ?? path;

// Adds titles for folders already in the library roots: nothing is searched, moved or renamed.
export function LibraryImport() {
  const [root, setRoot] = useState<LibraryRoot>("movies");
  const [proposals, setProposals] = useState<ImportProposals | null>(null);
  const [error, setError] = useState<string | null>(null);
  // The chosen match per folder; it starts as the proposed one.
  const [matches, setMatches] = useState<ReadonlyMap<string, DiscoveryItem | null>>(new Map());
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [changing, setChanging] = useState<ImportProposal | null>(null);
  const [importing, setImporting] = useState(false);
  const [outcomes, setOutcomes] = useState<ImportOutcome[] | null>(null);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    let current = true;
    api.importProposals(root)
      .then((result) => {
        if (!current) return;
        setProposals(result);
        setMatches(new Map(result.items.map((p) => [p.folder, p.match])));
        setSelected(new Set(result.items.filter((p) => p.match && (p.confidence === "High" || p.confidence === "Medium")).map((p) => p.folder)));
      })
      .catch((e: unknown) => { if (current) setError(errorMessage(e)); });
    return () => { current = false; };
  }, [root, reload]);

  const items = proposals?.items ?? [];
  const importable = items.filter((p) => matches.get(p.folder));
  const chosen = importable.filter((p) => selected.has(p.folder));

  const toggle = (folder: string) => setSelected((s) => {
    const next = new Set(s);
    if (!next.delete(folder)) next.add(folder);
    return next;
  });

  async function importChosen() {
    setImporting(true);
    setError(null);
    try {
      setOutcomes(await api.importFolders(chosen.map((p) => {
        const match = matches.get(p.folder);
        return match?.kind === "Movie" ? { folder: p.folder, tmdbId: match.tmdbId ?? undefined } : { folder: p.folder, tvdbId: match?.tvdbId ?? undefined };
      })));
      setReload((n) => n + 1);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setImporting(false);
    }
  }

  return (
    <>
      <PageHeader title="Library import" description="Folders in your library that no title owns yet. Importing adds the title with its files; nothing is searched, moved or renamed." />

      <div className="mb-6 flex flex-wrap items-center gap-3">
        <Segmented label="Library folder" value={root} onChange={(r) => { setRoot(r); setProposals(null); setError(null); setOutcomes(null); }} options={roots} />
        {proposals && (
          <p className="text-sm text-ink-3">
            {proposals.total === 0 ? "Every folder belongs to a title." : proposals.total > items.length
              ? `Showing ${items.length} of ${proposals.total} folders. Import these to see the next ones.`
              : `${proposals.total} ${proposals.total === 1 ? "folder" : "folders"} to import`}
          </p>
        )}
      </div>

      <div className="space-y-4">
        {error && <Alert>{error}</Alert>}
        {outcomes && <Outcomes outcomes={outcomes} onDismiss={() => setOutcomes(null)} />}

        {!proposals && !error ? (
          <div className="space-y-2">{Array.from({ length: 6 }, (_, i) => <Skeleton key={i} className="h-20 rounded-card" />)}</div>
        ) : proposals && items.length === 0 ? (
          <Empty icon="folder" title="Nothing to import here">
            <p>Every folder in this library already belongs to a title. New folders show up here as soon as they appear on disk.</p>
          </Empty>
        ) : proposals && (
          <>
            <div className="flex flex-wrap items-center gap-3 px-1 text-sm">
              <label className="flex cursor-pointer items-center gap-2.5">
                <input type="checkbox" className="size-4 cursor-pointer accent-white"
                  checked={chosen.length > 0 && chosen.length === importable.length}
                  ref={(box) => { if (box) box.indeterminate = chosen.length > 0 && chosen.length < importable.length; }}
                  disabled={importable.length === 0}
                  onChange={(e) => setSelected(new Set(e.target.checked ? importable.map((p) => p.folder) : []))} />
                Select all with a match
              </label>
              <span className="ml-auto text-ink-3 tabular-nums">{chosen.length} selected</span>
            </div>

            <ul className="divide-y divide-hair-2 overflow-hidden rounded-tile border border-hair bg-fill">
              {items.map((p) => (
                <Row key={p.folder} proposal={p} match={matches.get(p.folder) ?? null} selected={selected.has(p.folder)}
                  onToggle={() => toggle(p.folder)} onChange={() => setChanging(p)} />
              ))}
            </ul>

            <div className="sticky bottom-[calc(4.5rem+env(safe-area-inset-bottom))] z-20 flex items-center justify-end gap-3 rounded-tile border border-hair bg-fill px-4 py-3 shadow-2xl shadow-black/40 backdrop-blur-xl lg:bottom-4">
              <span className="mr-auto text-sm text-ink-3">{chosen.length === 0 ? "Select the folders to import." : `${chosen.length} ${chosen.length === 1 ? "title" : "titles"} added with their files, no search.`}</span>
              <Button variant="primary" onClick={importChosen} disabled={importing || chosen.length === 0}>
                {importing ? <Spinner /> : <Icon name="download" size={16} />}Import {chosen.length > 0 && chosen.length}
              </Button>
            </div>
          </>
        )}
      </div>

      {changing && (
        <MatchPicker proposal={changing} current={matches.get(changing.folder) ?? null} root={root} onClose={() => setChanging(null)}
          onPick={(match) => {
            setMatches((m) => new Map(m).set(changing.folder, match));
            setSelected((s) => new Set(s).add(changing.folder));
            setChanging(null);
          }} />
      )}
    </>
  );
}

function Row({ proposal, match, selected, onToggle, onChange }: {
  proposal: ImportProposal;
  match: DiscoveryItem | null;
  selected: boolean;
  onToggle: () => void;
  onChange: () => void;
}) {
  const changed = match !== proposal.match;
  const badge = changed ? { tone: "live" as Tone, label: "Chosen by you" } : confidence[proposal.confidence];
  const name = folderName(proposal.folder);
  return (
    <li className={`flex items-center gap-3 px-4 py-3 transition sm:gap-4 ${selected ? "bg-fill" : ""}`}>
      <input type="checkbox" checked={selected} onChange={onToggle} disabled={!match} aria-label={`Import ${name}`}
        className="size-4 shrink-0 cursor-pointer accent-white disabled:cursor-not-allowed" />
      <PosterArt src={match?.poster ?? null} title={match?.title ?? name} className="w-11 shrink-0 rounded-md" />
      <div className="grid min-w-0 flex-1 gap-1 md:grid-cols-[minmax(0,1fr)_minmax(0,1.2fr)] md:items-center md:gap-6">
        <div className="min-w-0">
          <p className="flex items-center gap-1.5 truncate text-sm font-medium" title={proposal.folder}><Icon name="folder" size={14} className="shrink-0 text-ink-3" />{name}</p>
        </div>
        <div className="flex min-w-0 flex-wrap items-center gap-x-2.5 gap-y-1">
          {match ? (
            <span className="min-w-0 truncate text-sm">
              <Icon name="chevronRight" size={14} className="mr-1 inline text-ink-3 max-md:hidden" />
              {match.title}{match.year && <span className="text-ink-3"> ({match.year})</span>}
            </span>
          ) : (
            <span className="text-sm text-ink-3">No title found for “{proposal.parsed.lookupTerm}”</span>
          )}
          <Badge tone={badge.tone}>{badge.label}</Badge>
        </div>
      </div>
      <Button variant="ghost" size="sm" onClick={onChange} aria-label={`Change the match of ${name}`}>
        <Icon name="search" size={14} /><span className="max-sm:hidden">{match ? "Change" : "Find"}</span>
      </Button>
    </li>
  );
}

function MatchPicker({ proposal, current, root, onPick, onClose }: {
  proposal: ImportProposal;
  current: DiscoveryItem | null;
  root: LibraryRoot;
  onPick: (match: DiscoveryItem) => void;
  onClose: () => void;
}) {
  const [term, setTerm] = useState(proposal.parsed.year ? `${proposal.parsed.title} ${proposal.parsed.year}` : proposal.parsed.title);
  const [results, setResults] = useState<DiscoveryItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const query = term.trim();

  useEffect(() => {
    if (query.length < 2) return;
    const abort = new AbortController();
    const timer = setTimeout(() => {
      setResults(null);
      api.lookup(query, abort.signal)
        .then((all) => { setResults(all.filter((i) => (root === "movies") === (i.kind === "Movie"))); setError(null); })
        .catch((e: unknown) => { if (!abort.signal.aborted) setError(errorMessage(e)); });
    }, 300);
    return () => { clearTimeout(timer); abort.abort(); };
  }, [query, root]);

  return (
    <Modal onClose={onClose} title="Change match" description={<span title={proposal.folder}>For the folder “{folderName(proposal.folder)}”</span>}>
      <div className="space-y-3 px-5 pb-5">
        <div className="relative">
          <Icon name="search" size={16} className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-ink-3" />
          <Input value={term} onChange={(e) => setTerm(e.target.value)} aria-label="Search a title" placeholder="Title, year, or tmdb:603 / tvdb:81189" className="pl-9" />
        </div>
        {error && <Alert>{error}</Alert>}
        {query.length < 2 ? null : !results && !error ? (
          <div className="space-y-2">{Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-16" />)}</div>
        ) : results && results.length === 0 ? (
          <p className="rounded-card border border-dashed border-hair px-5 py-8 text-center text-sm text-ink-3">No title matches “{query}”. Try the original title or an id.</p>
        ) : results && (
          <ul className="max-h-[50vh] divide-y divide-hair-2 overflow-y-auto rounded-card border border-hair">
            {results.map((item) => {
              const isCurrent = item.kind === current?.kind && item.tmdbId === current.tmdbId && item.tvdbId === current.tvdbId;
              return (
                <li key={`${item.kind}${item.tmdbId}${item.tvdbId}`}>
                  <button onClick={() => onPick(item)} disabled={item.inLibrary} aria-current={isCurrent || undefined}
                    className="flex w-full cursor-pointer items-center gap-3 px-3 py-2.5 text-left transition hover:bg-fill-2 disabled:cursor-not-allowed disabled:opacity-50">
                    <PosterArt src={item.poster} title={item.title} className="w-9 shrink-0 rounded-md" />
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm font-medium">{item.title}</span>
                      <span className="block text-xs text-ink-3">{item.year ?? "Year unknown"}{item.inLibrary && ", already in your library"}</span>
                    </span>
                    {isCurrent && <Badge tone="live"><Icon name="check" size={11} />Current</Badge>}
                  </button>
                </li>
              );
            })}
          </ul>
        )}
      </div>
    </Modal>
  );
}

function Outcomes({ outcomes, onDismiss }: { outcomes: ImportOutcome[]; onDismiss: () => void }) {
  const failed = outcomes.filter((o) => o.error);
  const added = outcomes.filter((o) => !o.error);
  return (
    <div role="status" className="animate-fade space-y-2 rounded-tile border border-hair bg-fill p-4 text-sm">
      <div className="flex items-center gap-2">
        <Icon name={failed.length ? "alert" : "check"} size={16} className={failed.length ? "text-warn" : "text-ok"} />
        <p className="flex-1">
          {added.length} {added.length === 1 ? "title" : "titles"} imported with {added.reduce((n, o) => n + o.files, 0)} files
          {failed.length > 0 && `, ${failed.length} failed`}.
        </p>
        <Button variant="ghost" size="sm" onClick={onDismiss} aria-label="Dismiss"><Icon name="x" size={14} /></Button>
      </div>
      <ul className="space-y-1 pl-6">
        {added.map((o) => (
          <li key={o.folder} className="truncate">
            <Link className="text-ink hover:underline" href={o.movieId !== null ? titleHref("movie", o.movieId) : titleHref("series", o.seriesId ?? 0)}>{folderName(o.folder)}</Link>
          </li>
        ))}
        {failed.map((o) => <li key={o.folder} className="text-bad">{folderName(o.folder)}: {o.error}</li>)}
      </ul>
    </div>
  );
}
