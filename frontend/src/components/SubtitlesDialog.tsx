"use client";

import { useCallback, useEffect, useState } from "react";
import { api, errorMessage } from "@/lib/api";
import type { SubtitleCandidate, SubtitleStatus, SubtitleTarget } from "@/lib/api";
import { realLanguages, useOptions } from "@/lib/options";
import { useActivityRefresh } from "@/components/Activity";
import { Icon } from "@/components/icons";
import { Alert, Badge, Button, Modal, Select, Skeleton, WellList } from "@/components/ui";

// The subtitles next to one file: what is there and missing, a provider search and a download per candidate.
export function SubtitlesDialog({ target, title, onClose }: { target: SubtitleTarget; title: string; onClose: () => void }) {
  const options = useOptions();
  const [status, setStatus] = useState<SubtitleStatus | null>(null);
  const [language, setLanguage] = useState("");
  const [candidates, setCandidates] = useState<SubtitleCandidate[] | null>(null);
  const [searching, setSearching] = useState(false);
  const [downloading, setDownloading] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    api.subtitles(target).then(setStatus).catch((e: unknown) => setError(errorMessage(e)));
  }, [target]);

  useEffect(load, [load]);

  async function search() {
    setSearching(true);
    setError(null);
    try {
      setCandidates(await api.searchSubtitles(target, language || undefined));
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setSearching(false);
    }
  }

  async function download(candidate: SubtitleCandidate) {
    setDownloading(candidate.id);
    setError(null);
    try {
      await api.downloadSubtitle(target, candidate);
      load();
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setDownloading(null);
    }
  }

  return (
    <Modal onClose={onClose} title={`Subtitles for ${title}`}
      description={status && (status.missing.length > 0 ? `Missing: ${status.missing.join(", ")}.` : "Every wanted language is there.")}>
      <div className="space-y-4 px-5 pb-5">
        {error && <Alert>{error}</Alert>}
        {!status ? <Skeleton className="h-10" /> : (
          <div className="flex flex-wrap gap-1.5">
            {status.present.length === 0 && <span className="text-meta text-ink-3">No subtitle next to the file yet.</span>}
            {status.present.map((s) => (
              <Badge key={s.file} tone="ok">{s.language}{s.hearingImpaired && " HI"}{s.forced && " forced"}</Badge>
            ))}
          </div>
        )}
        <div className="flex flex-wrap items-center gap-2">
          <Select value={language} onChange={(e) => setLanguage(e.target.value)} aria-label="Subtitle language" className="w-56">
            <option value="">Wanted languages</option>
            {options && realLanguages(options).map((l) => <option key={l.id} value={l.name}>{l.name}</option>)}
          </Select>
          <Button variant="primary" icon="search" loading={searching} onClick={search}>Search</Button>
        </div>
        {candidates && (candidates.length === 0 ? (
          <p className="rounded-card border border-dashed border-hair px-5 py-8 text-center text-meta text-ink-3">No provider has subtitles for this file.</p>
        ) : (
          <WellList>
            {candidates.map((c) => (
              <li key={`${c.provider}${c.id}`} className="flex items-center gap-3 px-4 py-2.5 text-body">
                <div className="min-w-0 flex-1">
                  <p className="truncate" title={c.release}>{c.release || "Unnamed release"}</p>
                  <p className="flex flex-wrap items-center gap-2 text-meta text-ink-3">
                    <span>{c.language}</span><span>{c.provider}</span><span>{c.downloads} downloads</span>
                    {c.hashMatch && <Badge tone="ok">Matches the file</Badge>}
                    {c.hearingImpaired && <Badge>HI</Badge>}
                    {c.forced && <Badge>Forced</Badge>}
                  </p>
                </div>
                <Button size="sm" icon="download" loading={downloading === c.id} onClick={() => download(c)} disabled={downloading !== null} aria-label={`Download ${c.release}`} />
              </li>
            ))}
          </WellList>
        ))}
      </div>
    </Modal>
  );
}

// A file's subtitles at a glance: present languages, then the missing ones, a button to open the dialog and one to search the
// missing ones. Reloads when reload changes and when a subtitle of the title is downloaded.
export function SubtitleSummary({ target, reload, onOpen, onSearchMissing }: {
  target: SubtitleTarget;
  reload?: number;
  onOpen?: () => void;
  onSearchMissing?: () => Promise<void>;
}) {
  const [status, setStatus] = useState<SubtitleStatus | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [searching, setSearching] = useState(false);

  const load = useCallback(() => {
    api.subtitles(target).then(setStatus).catch((e: unknown) => setError(errorMessage(e)));
  }, [target]);
  useEffect(load, [load, reload]);
  useActivityRefresh((a) => a.kind === "SubtitleDownloaded" && (!("movieId" in target) || a.movieId === target.movieId), load);

  return (
    <div className="space-y-2.5">
      <div className="flex items-center gap-3">
        <span className="flex-1 text-meta text-ink-3">Subtitles</span>
        {onSearchMissing && status && status.missing.length > 0 && (
          <Button size="sm" variant="ghost" icon="bolt" loading={searching}
            onClick={() => { setSearching(true); void onSearchMissing().finally(() => setSearching(false)); }}>Search missing</Button>
        )}
        {onOpen && <Button size="sm" variant="ghost" icon="search" onClick={onOpen}>Find</Button>}
      </div>
      {error ? <p className="text-meta text-bad">{error}</p> : !status ? <Skeleton className="h-6" /> : (
        <div className="flex flex-wrap gap-1.5">
          {status.present.map((s) => <Badge key={s.file} tone="ok"><Icon name="check" size={14} />{s.language}{s.hearingImpaired && " HI"}{s.forced && " forced"}</Badge>)}
          {status.missing.map((l) => <Badge key={l} tone="warn">{l} missing</Badge>)}
          {status.present.length === 0 && status.missing.length === 0 && <span className="text-meta text-ink-3">None wanted for this title.</span>}
        </div>
      )}
    </div>
  );
}
