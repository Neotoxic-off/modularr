"use client";

import { useCallback, useEffect, useState } from "react";
import { api, errorMessage } from "@/lib/api";
import type { SubtitleCandidate, SubtitleStatus, SubtitleTarget } from "@/lib/api";
import { realLanguages, useOptions } from "@/lib/options";
import { Icon } from "@/components/icons";
import { Alert, Badge, Button, Modal, Select, Skeleton, Spinner } from "@/components/ui";

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
            {status.present.length === 0 && <span className="text-sm text-ink-3">No subtitle next to the file yet.</span>}
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
          <Button variant="primary" onClick={search} disabled={searching}>{searching ? <Spinner /> : <Icon name="search" size={16} />}Search</Button>
        </div>
        {candidates && (candidates.length === 0 ? (
          <p className="rounded-card border border-dashed border-hair px-5 py-8 text-center text-sm text-ink-3">No provider has subtitles for this file.</p>
        ) : (
          <ul className="divide-y divide-hair-2 overflow-hidden rounded-card border border-hair">
            {candidates.map((c) => (
              <li key={`${c.provider}${c.id}`} className="flex items-center gap-3 px-4 py-2.5 text-sm">
                <div className="min-w-0 flex-1">
                  <p className="truncate" title={c.release}>{c.release || "Unnamed release"}</p>
                  <p className="flex flex-wrap items-center gap-2 text-xs text-ink-3">
                    <span>{c.language}</span><span>{c.provider}</span><span>{c.downloads} downloads</span>
                    {c.hashMatch && <Badge tone="ok">Matches the file</Badge>}
                    {c.hearingImpaired && <Badge>HI</Badge>}
                    {c.forced && <Badge>Forced</Badge>}
                  </p>
                </div>
                <Button size="sm" onClick={() => download(c)} disabled={downloading !== null} aria-label={`Download ${c.release}`}>
                  {downloading === c.id ? <Spinner /> : <Icon name="download" size={14} />}
                </Button>
              </li>
            ))}
          </ul>
        ))}
      </div>
    </Modal>
  );
}

// A file's subtitles at a glance: present languages, then the missing ones, and a button to open the dialog.
export function SubtitleSummary({ target, onOpen }: { target: SubtitleTarget; onOpen: () => void }) {
  const [status, setStatus] = useState<SubtitleStatus | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.subtitles(target).then(setStatus).catch((e: unknown) => setError(errorMessage(e)));
  }, [target]);

  return (
    <div className="space-y-2.5">
      <div className="flex items-center justify-between gap-3">
        <span className="text-sm text-ink-3">Subtitles</span>
        <Button size="sm" variant="ghost" onClick={onOpen}><Icon name="search" size={14} />Find</Button>
      </div>
      {error ? <p className="text-xs text-bad">{error}</p> : !status ? <Skeleton className="h-6" /> : (
        <div className="flex flex-wrap gap-1.5">
          {status.present.map((s) => <Badge key={s.file} tone="ok"><Icon name="check" size={11} />{s.language}{s.hearingImpaired && " HI"}{s.forced && " forced"}</Badge>)}
          {status.missing.map((l) => <Badge key={l} tone="warn">{l} missing</Badge>)}
          {status.present.length === 0 && status.missing.length === 0 && <span className="text-xs text-ink-3">None wanted for this title.</span>}
        </div>
      )}
    </div>
  );
}
