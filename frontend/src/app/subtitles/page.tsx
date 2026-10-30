"use client";

import { useCallback, useEffect, useState } from "react";
import { api, errorMessage } from "@/lib/api";
import type { Activity, Kind, LanguageState, SeasonSubtitles, SubtitleTarget, TitleSubtitles } from "@/lib/api";
import { realLanguages, useOptions } from "@/lib/options";
import { titleKey, useActivityRefresh } from "@/components/Activity";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { PosterArt } from "@/components/Poster";
import { SubtitlesDialog } from "@/components/SubtitlesDialog";
import { useToast } from "@/components/Toasts";
import { Alert, Badge, Button, Empty, IconButton, Input, PageHeader, PickList, Segmented, Skeleton, Switch, WellList, toneDot } from "@/components/ui";

type Filter = "all" | Kind;

const kinds: readonly { value: Filter; label: string }[] = [
  { value: "all", label: "All" }, { value: "movie", label: "Movies" }, { value: "series", label: "Series" }, { value: "anime", label: "Anime" },
];

const kindOf = (t: TitleSubtitles) => t.kind.toLowerCase() as Kind;
const complete = (t: TitleSubtitles) => t.coverage.every((c) => c.covered === t.files);
const ids = (t: TitleSubtitles) => (t.kind === "Movie" ? { movieId: t.id, seriesId: null } : { movieId: null, seriesId: t.id });
const downloaded = (a: Activity) => a.kind === "SubtitleDownloaded";

// Subtitles by title, like Bazarr: the wanted languages of each, how many files have them, and each file's state.
export default function SubtitlesPage() {
  const [titles, setTitles] = useState<TitleSubtitles[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [text, setText] = useState("");
  const [kind, setKind] = useState<Filter>("all");
  const [onlyMissing, setOnlyMissing] = useState(false);
  const [open, setOpen] = useState<string | null>(null);

  const load = useCallback(() => {
    api.subtitleOverview().then(setTitles).catch((e: unknown) => setError(errorMessage(e)));
  }, []);
  useEffect(load, [load]);
  useActivityRefresh(downloaded, load);

  const needle = text.trim().toLowerCase();
  const shown = titles?.filter((t) => (kind === "all" || kindOf(t) === kind) && (!onlyMissing || !complete(t)) && t.title.toLowerCase().includes(needle));

  return (
    <div className="mx-auto max-w-4xl">
      <PageHeader title="Subtitles" />
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <Input icon="search" value={text} onChange={(e) => setText(e.target.value)} placeholder="Search titles" aria-label="Search titles" className="min-w-48 flex-1" />
        <Segmented label="Kind" value={kind} options={kinds} onChange={setKind} />
        <div><Switch checked={onlyMissing} onChange={setOnlyMissing} label="Only missing" /></div>
      </div>
      {error && <Alert>{error}</Alert>}
      {!shown ? (!error && <Skeleton className="h-40" />) : shown.length === 0 ? (
        <Empty icon="subtitles" title={titles?.length ? "No title matches" : "The library is empty"} />
      ) : (
        <WellList label="Titles">
          {shown.map((t) => {
            const key = `${t.kind}:${t.id}`;
            return <TitleRow key={key} title={t} open={open === key} onToggle={() => setOpen(open === key ? null : key)} onChanged={load} />;
          })}
        </WellList>
      )}
    </div>
  );
}

function TitleRow({ title: t, open, onToggle, onChanged }: { title: TitleSubtitles; open: boolean; onToggle: () => void; onChanged: () => void }) {
  const admin = useSession().user.role === "Admin";
  const options = useOptions();
  const toast = useToast();
  const kind = kindOf(t);
  const [editing, setEditing] = useState(false);
  const [busy, setBusy] = useState(false);

  async function run(action: () => Promise<unknown>, done?: string) {
    const key = titleKey(ids(t)) ?? t.title;
    setBusy(true);
    try {
      await action();
      if (done) toast({ key, icon: "subtitles", tone: "seek", title: t.title, message: done });
      onChanged();
    } catch (e) {
      toast({ key, icon: "alert", tone: "bad", title: t.title, message: errorMessage(e) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <li>
      <div className="flex items-center gap-3 px-4 py-2.5">
        <button type="button" onClick={onToggle} aria-expanded={open} className="flex min-w-0 flex-1 cursor-pointer items-center gap-3 text-left">
          <PosterArt src={t.poster} title={t.title} className="w-10 shrink-0 rounded-chip" />
          <span className="min-w-0 flex-1">
            <span className="block truncate text-body font-medium">{t.title} <span className="text-ink-3">{t.year || ""}</span></span>
            <span className="mt-1 flex flex-wrap gap-1.5">
              {t.coverage.length === 0 && <span className="text-meta text-ink-3">No subtitles wanted</span>}
              {t.coverage.map((c) => (
                <Badge key={c.language} tone={c.covered === t.files ? "ok" : "warn"}>
                  <span title={c.language}>{(c.code ?? c.language).toUpperCase()}</span>
                  <span className="tabular-nums">{c.covered}/{t.files}</span>
                </Badge>
              ))}
            </span>
          </span>
          <Icon name="chevronDown" size={16} className={`shrink-0 text-ink-3 transition ${open ? "rotate-180" : ""}`} />
        </button>
        {admin && (
          <>
            <IconButton variant="ghost" icon="languages" label={`Subtitle languages for ${t.title}`} aria-pressed={editing} onClick={() => setEditing(!editing)} />
            <Button size="sm" icon="search" loading={busy} disabled={t.files === 0 || complete(t)}
              onClick={() => void run(() => api.searchMissingSubtitles(kind, t.id), "Searching the missing subtitles.")}>Search missing</Button>
          </>
        )}
      </div>
      {editing && options && (
        <div className="space-y-2 border-t border-hair-2 px-4 py-3">
          <PickList label="Wanted subtitles" values={t.wanted} choices={realLanguages(options).map((l) => l.name)} empty="No subtitles wanted"
            onChange={(subtitles) => void run(() => api.update(kind, t.id, { subtitles }))} />
          {t.ownLanguages && <Button size="sm" variant="ghost" icon="undo" onClick={() => void run(() => api.update(kind, t.id, { subtitles: null }))}>Use category default</Button>}
        </div>
      )}
      {open && <Files title={t} />}
    </li>
  );
}

// The title's files: a movie's one, or a series' episodes by season.
function Files({ title: t }: { title: TitleSubtitles }) {
  const admin = useSession().user.role === "Admin";
  const [seasons, setSeasons] = useState<SeasonSubtitles[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [dialog, setDialog] = useState<{ target: SubtitleTarget; title: string } | null>(null);

  // Keyed on the wanted list too, so an edit of the languages reloads the states.
  const wanted = t.wanted.join();
  const load = useCallback(() => {
    const loaded = t.kind === "Movie"
      ? api.movieSubtitles(t.id).then((m) => (m.languages ? [{ number: -1, episodes: [{ id: t.id, episodeNumber: 0, title: null, languages: m.languages }] }] : []))
      : api.seriesSubtitles(t.id).then((s) => s.seasons);
    loaded.then(setSeasons).catch((e: unknown) => setError(errorMessage(e)));
  }, [t.kind, t.id]);
  useEffect(load, [load, wanted]);
  useActivityRefresh((a) => downloaded(a) && titleKey(a) === titleKey(ids(t)), load);

  if (error) return <div className="px-4 pb-3"><Alert>{error}</Alert></div>;
  if (!seasons) return <div className="px-4 pb-3"><Skeleton className="h-10" /></div>;
  return (
    <div className="space-y-3 border-t border-hair-2 px-4 py-3">
      {seasons.length === 0 && <p className="text-meta text-ink-3">No file on disk yet.</p>}
      {seasons.map((season) => (
        <div key={season.number}>
          {season.number >= 0 && <p className="mb-1 text-meta font-semibold text-ink-2">{season.number === 0 ? "Specials" : `Season ${season.number}`}</p>}
          <ul className="divide-y divide-hair-2">
            {season.episodes.map((e) => {
              const name = t.kind === "Movie" ? t.title : `${t.title} S${String(season.number).padStart(2, "0")}E${String(e.episodeNumber).padStart(2, "0")}`;
              return (
                <li key={e.id} className="flex items-center gap-3 py-1.5">
                  <span className="min-w-0 flex-1 truncate text-body">
                    {t.kind === "Movie" ? "Movie file" : <><span className="text-ink-3 tabular-nums">{e.episodeNumber}</span> {e.title}</>}
                  </span>
                  <span className="flex flex-wrap justify-end gap-1.5">{e.languages.map((l) => <LanguageMark key={l.language} state={l} />)}</span>
                  {admin && <IconButton variant="ghost" icon="subtitles" label={`Subtitles for ${name}`}
                    onClick={() => setDialog({ target: t.kind === "Movie" ? { movieId: t.id } : { episodeId: e.id }, title: name })} />}
                </li>
              );
            })}
          </ul>
        </div>
      ))}
      {dialog && <SubtitlesDialog target={dialog.target} title={dialog.title} onClose={() => { setDialog(null); load(); }} />}
    </div>
  );
}

function LanguageMark({ state: l }: { state: LanguageState }) {
  const flags = `${l.hearingImpaired ? " HI" : ""}${l.forced ? " forced" : ""}`;
  if (l.state === "Missing") return <Badge tone="warn">{l.language}{l.forced && " (forced only)"}</Badge>;
  return (
    <span className="inline-flex items-center gap-1.5 text-meta text-ink-2" title={l.state === "Audio" ? `${l.language} audio, no subtitle needed` : `${l.language}${flags}`}>
      <span className={`size-1.5 rounded-full ${toneDot[l.state === "Present" ? "ok" : "neutral"]}`} />
      {l.language}{l.state === "Present" && flags}
    </span>
  );
}
