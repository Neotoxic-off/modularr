"use client";

import { useEffect, useState } from "react";
import { api, episodeCode, errorMessage, formatAgo, formatSize } from "@/lib/api";
import type { Decision, LibraryItem, SearchResult } from "@/lib/api";
import { Alert, Badge, Button, ButtonLink, Modal, Skeleton, WellList } from "@/components/ui";

// Interactive search: every release the indexers return, approved ones first, each one grabbable.
export function ReleasesDialog({ item, season, episode, onClose }: { item: LibraryItem; season?: number; episode?: number; onClose: () => void }) {
  const [result, setResult] = useState<SearchResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [grabbing, setGrabbing] = useState<string | null>(null);
  const [grabbed, setGrabbed] = useState<ReadonlySet<string>>(new Set());
  // A failed grab shows on its own row.
  const [failed, setFailed] = useState<{ id: string; message: string } | null>(null);
  const [showRejected, setShowRejected] = useState(false);

  useEffect(() => {
    api.releases(item, season, episode).then(setResult).catch((e: unknown) => setError(errorMessage(e)));
  }, [item, season, episode]);

  async function grab(decision: Decision) {
    setGrabbing(decision.release.id);
    setFailed(null);
    try {
      await api.grab(item, decision.release);
      setGrabbed((ids) => new Set(ids).add(decision.release.id));
    } catch (e) {
      setFailed({ id: decision.release.id, message: errorMessage(e) });
    } finally {
      setGrabbing(null);
    }
  }

  const decisions = result?.decisions ?? [];
  const rejected = decisions.filter((d) => !d.approved).length;
  const shown = showRejected ? decisions : decisions.filter((d) => d.approved);

  return (
    <Modal onClose={onClose} title={`Releases for ${item.title}${season === undefined ? "" : episode === undefined ? `, season ${season}` : ` ${episodeCode({ seasonNumber: season, episodeNumber: episode })}`}`}
      description={result
        ? `${result.approved} of ${result.releases} releases match your profile, from ${result.indexersSearched} indexers.`
        : "Searching every enabled indexer. This can take a few seconds."}
      footer={grabbed.size > 0 ? <ButtonLink href="/downloads/" variant="ghost">View in downloads</ButtonLink> : undefined}>
      <div className="space-y-3 px-5 pb-5">
        {error && <Alert>{error}</Alert>}
        {result && result.errors.length > 0 && (
          <Alert tone="warn">{result.errors.length} {result.errors.length === 1 ? "indexer" : "indexers"} failed: {result.errors.map((e) => e.name).join(", ")}</Alert>
        )}
        {!result && !error && Array.from({ length: 5 }, (_, i) => <Skeleton key={i} className="h-16" />)}
        {result && (
          <>
            {rejected > 0 && (
              <div className="flex justify-end">
                <Button variant="ghost" size="sm" icon={showRejected ? "eyeOff" : "eye"} onClick={() => setShowRejected((s) => !s)}>
                  {showRejected ? "Hide" : "Show"} {rejected} rejected
                </Button>
              </div>
            )}
            {shown.length === 0 ? (
              <p className="rounded-card border border-dashed border-hair px-5 py-10 text-center text-meta text-ink-3">
                {decisions.length === 0 ? "No indexer returned a release for this title." : "No release matches your quality profile and version. Show the rejected ones to grab one anyway."}
              </p>
            ) : (
              <WellList>
                {shown.map((d) => {
                  const done = grabbed.has(d.release.id);
                  return (
                    <li key={d.release.id} className={`flex items-center gap-3 px-4 py-3 ${d.approved ? "" : "bg-sunk"}`}>
                      <div className="min-w-0 flex-1">
                        <p className={`line-clamp-2 break-all text-body ${d.approved ? "" : "text-ink-3"}`}>{d.release.title}</p>
                        <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-meta text-ink-3">
                          <Badge>{d.quality}</Badge>
                          <Badge>{d.version ?? d.languages.join(", ")}</Badge>
                          <span className="tabular-nums">{formatSize(d.release.size)}</span>
                          <span className="tabular-nums">{d.release.seeders ?? "?"} seeders · {d.release.peers ?? "?"} peers</span>
                        </div>
                        <p className="mt-1 text-meta text-ink-3">
                          {[d.release.indexer, d.release.publishDate && formatAgo(d.release.publishDate), d.languages.length > 0 && `Audio: ${d.languages.join(", ")}`,
                            d.subtitles.length > 0 && `Subtitles: ${d.subtitles.join(", ")}`].filter(Boolean).join(" · ")}
                        </p>
                        {!d.approved && d.rejections.length > 0 && (
                          <p className="mt-1 text-meta text-warn">{d.rejections.map((r) => r.message).join(" ")}</p>
                        )}
                        {failed?.id === d.release.id && <p role="alert" className="mt-1 text-meta text-bad">{failed.message}</p>}
                      </div>
                      <Button variant={done ? "ghost" : d.approved ? "primary" : "glass"} size="sm" icon={done ? "check" : "download"}
                        loading={grabbing === d.release.id} onClick={() => grab(d)} disabled={grabbing !== null || done} aria-label={`Grab ${d.release.title}`}>
                        {done ? "Grabbed" : "Grab"}
                      </Button>
                    </li>
                  );
                })}
              </WellList>
            )}
          </>
        )}
      </div>
    </Modal>
  );
}
