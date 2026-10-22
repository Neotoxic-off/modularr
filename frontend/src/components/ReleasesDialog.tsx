"use client";

import { useEffect, useState } from "react";
import { api, errorMessage, formatAgo, formatSize } from "@/lib/api";
import type { Decision, LibraryItem, SearchResult } from "@/lib/api";
import { Icon } from "@/components/icons";
import { Alert, Badge, Button, Modal, Skeleton, Spinner } from "@/components/ui";

// Interactive search: every release the indexers return, approved ones first, each one grabbable.
export function ReleasesDialog({ item, season, onClose }: { item: LibraryItem; season?: number; onClose: () => void }) {
  const [result, setResult] = useState<SearchResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [grabbing, setGrabbing] = useState<string | null>(null);
  const [grabbed, setGrabbed] = useState<ReadonlySet<string>>(new Set());
  const [showRejected, setShowRejected] = useState(false);

  useEffect(() => {
    api.releases(item, season).then(setResult).catch((e: unknown) => setError(errorMessage(e)));
  }, [item, season]);

  async function grab(decision: Decision) {
    setGrabbing(decision.release.id);
    setError(null);
    try {
      await api.grab(item, decision.release);
      setGrabbed((ids) => new Set(ids).add(decision.release.id));
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setGrabbing(null);
    }
  }

  const decisions = result?.decisions ?? [];
  const rejected = decisions.filter((d) => !d.approved).length;
  const shown = showRejected ? decisions : decisions.filter((d) => d.approved);

  return (
    <Modal size="lg" onClose={onClose} title={`Releases for ${item.title}${season === undefined ? "" : `, season ${season}`}`}
      description={result
        ? `${result.approved} of ${result.releases} releases match your profile, from ${result.indexersSearched} indexers.`
        : "Searching every enabled indexer. This can take a few seconds."}>
      <div className="space-y-3 px-5 pb-5">
        {error && <Alert>{error}</Alert>}
        {result && result.errors.length > 0 && (
          <Alert tone="amber">{result.errors.length} {result.errors.length === 1 ? "indexer" : "indexers"} failed: {result.errors.map((e) => e.name).join(", ")}</Alert>
        )}
        {!result && !error && Array.from({ length: 5 }, (_, i) => <Skeleton key={i} className="h-16" />)}
        {result && (
          <>
            {rejected > 0 && (
              <div className="flex justify-end">
                <Button variant="ghost" size="sm" onClick={() => setShowRejected((s) => !s)}>
                  <Icon name={showRejected ? "eyeOff" : "eye"} size={14} />{showRejected ? "Hide" : "Show"} {rejected} rejected
                </Button>
              </div>
            )}
            {shown.length === 0 ? (
              <p className="rounded-xl border border-dashed border-line px-5 py-10 text-center text-sm text-muted">
                {decisions.length === 0 ? "No indexer returned a release for this title." : "No release matches your quality profile and version. Show the rejected ones to grab one anyway."}
              </p>
            ) : (
              <ul className="divide-y divide-line overflow-hidden rounded-xl border border-line">
                {shown.map((d) => (
                  <li key={d.release.id} className={`flex items-center gap-3 px-4 py-3 ${d.approved ? "" : "bg-background/40"}`}>
                    <div className="min-w-0 flex-1">
                      <p className={`truncate text-sm ${d.approved ? "" : "text-muted"}`} title={d.release.title}>{d.release.title}</p>
                      <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted">
                        <Badge tone={d.approved ? "accent" : "neutral"}>{d.quality}</Badge>
                        {d.languages.length > 0 && <span>{d.languages.join(", ")}</span>}
                        {d.subtitles.length > 0 && <span>Subtitles {d.subtitles.join(", ")}</span>}
                        <span className="tabular-nums">{formatSize(d.release.size)}</span>
                        <span className="tabular-nums" title="Seeders">{d.release.seeders ?? "?"} seeders</span>
                        <span>{d.release.indexer}</span>
                        {d.release.publishDate && <span>{formatAgo(d.release.publishDate)}</span>}
                      </div>
                      {!d.approved && d.rejections.length > 0 && (
                        <p className="mt-1 truncate text-xs text-amber-200/80" title={d.rejections.map((r) => r.message).join("\n")}>{d.rejections.map((r) => r.message).join(" ")}</p>
                      )}
                    </div>
                    <Button variant={grabbed.has(d.release.id) ? "ghost" : d.approved ? "primary" : "secondary"} size="sm"
                      onClick={() => grab(d)} disabled={grabbing !== null || grabbed.has(d.release.id)} aria-label={`Download ${d.release.title}`}>
                      {grabbing === d.release.id ? <Spinner /> : grabbed.has(d.release.id) ? <><Icon name="check" size={14} />Sent</> : <><Icon name="download" size={14} />Grab</>}
                    </Button>
                  </li>
                ))}
              </ul>
            )}
          </>
        )}
      </div>
    </Modal>
  );
}
