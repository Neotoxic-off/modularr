"use client";

import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { api, errorMessage } from "@/lib/api";
import type { Category, MediaSettings as Media, SubtitleSettings, VersionRule } from "@/lib/api";
import { realLanguages, reloadOptions, useOptions, versionLabel } from "@/lib/options";
import { useDraft } from "@/components/settings/draft";
import { Alert, Button, Field, IconButton, Input, PickList, SaveBar, Section, Select, Skeleton, Switch, Well, useAction } from "@/components/ui";

const categories: readonly { key: "movies" | "series" | "anime"; label: string }[] = [
  { key: "movies", label: "Movies" },
  { key: "series", label: "Series" },
  { key: "anime", label: "Anime" },
];

const categoryNames: Record<Category, string> = { Movie: "Movies", Series: "Series", Anime: "Anime" };

// Versions, wanted subtitles and the subtitle source: the media settings and the subtitle settings, saved together.
export function Languages() {
  const options = useOptions();
  const [base, setBase] = useState<Media | null>(null);
  const [draft, setMedia] = useState<Media | null>(null);
  const saved = base ?? options?.media;
  const media = draft ?? saved;
  const [subs, setSubs, loadSubs, subsDirty] = useDraft<SubtitleSettings>();
  const [loadError, setLoadError] = useState<string | null>(null);
  const { busy, error, run } = useAction();

  useEffect(() => {
    api.subtitleSettings().then(loadSubs).catch((e: unknown) => setLoadError(errorMessage(e)));
  }, [loadSubs]);

  if (!subs || !options || !media) return loadError ? <Alert>{loadError}</Alert> : <Skeleton className="h-96 rounded-tile" />;

  const mediaDirty = draft !== null && JSON.stringify(draft) !== JSON.stringify(saved);
  const versions = options.versions;
  // Conditions match an item's real original language: Any and Original make no sense there.
  const languages = realLanguages(options).map((l) => l.name);
  const account = subs.openSubtitles;
  const setAccount = (change: Partial<SubtitleSettings["openSubtitles"]>) => setSubs({ ...subs, openSubtitles: { ...account, ...change } });

  const save = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => {
      if (mediaDirty) {
        const r = await api.saveMedia(media);
        setBase(r);
        setMedia(r);
        reloadOptions();
      }
      if (subsDirty) loadSubs(await api.saveSubtitleSettings(subs));
    });
  };

  const setRule = (index: number, rule: VersionRule) => setMedia({ ...media, exceptions: media.exceptions.map((r, i) => (i === index ? rule : r)) });
  const moveRule = (index: number, step: number) => {
    const rules = [...media.exceptions];
    rules.splice(index + step, 0, ...rules.splice(index, 1));
    setMedia({ ...media, exceptions: rules });
  };

  return (
    <form onSubmit={save} className="space-y-6">
      <Section title="Default version" description="Used when nothing more specific applies.">
        <div className="max-w-sm">
          <Select value={media.defaultVersion} onChange={(e) => setMedia({ ...media, defaultVersion: e.target.value })} aria-label="Default version">
            {versions.map((v) => <option key={v} value={v}>{versionLabel(v)}</option>)}
          </Select>
        </div>
        <p className="mt-3 text-meta text-ink-3">“Any version” turns the check off. VO follows each title’s own original language; VF also covers VFF, TRUEFRENCH and FRENCH releases.</p>
      </Section>

      <Section title="Version exceptions" description="Checked from top to bottom; the first rule that matches a title wins over the category and default version."
        actions={<Button type="button" size="sm" icon="plus" onClick={() => setMedia({ ...media, exceptions: [...media.exceptions, { category: "Anime", originalLanguage: null, version: "VOSTFR" }] })}>
          Add rule
        </Button>}>
        {media.exceptions.length === 0 ? (
          <p className="text-meta text-ink-3">No exceptions. For example, add one so anime comes in VOSTFR.</p>
        ) : (
          <ol className="space-y-2">
            {media.exceptions.map((rule, i) => (
              <li key={i}>
                <Well className="flex flex-wrap items-center gap-2 !p-2.5 text-meta">
                  <span className="w-5 text-center text-meta text-ink-3 tabular-nums">{i + 1}</span>
                  <span className="text-ink-3">When</span>
                  <Select value={rule.category ?? ""} aria-label="Category" className="w-32"
                    onChange={(e) => setRule(i, { ...rule, category: (e.target.value || null) as Category | null })}>
                    <option value="">any category</option>
                    {(Object.keys(categoryNames) as Category[]).map((c) => <option key={c} value={c}>{categoryNames[c]}</option>)}
                  </Select>
                  <span className="text-ink-3">in</span>
                  <Select value={rule.originalLanguage ?? ""} aria-label="Original language" className="w-40"
                    onChange={(e) => setRule(i, { ...rule, originalLanguage: e.target.value || null })}>
                    <option value="">any original language</option>
                    {languages.map((l) => <option key={l} value={l}>{l}</option>)}
                  </Select>
                  <span className="text-ink-3">want</span>
                  <Select value={rule.version} aria-label="Wanted version" className="w-48"
                    onChange={(e) => setRule(i, { ...rule, version: e.target.value })}>
                    {versions.map((v) => <option key={v} value={v}>{versionLabel(v)}</option>)}
                  </Select>
                  <span className="ml-auto flex">
                    <IconButton type="button" variant="ghost" icon="up" label={`Move rule ${i + 1} up`} disabled={i === 0} onClick={() => moveRule(i, -1)} />
                    <IconButton type="button" variant="ghost" icon="down" label={`Move rule ${i + 1} down`} disabled={i === media.exceptions.length - 1} onClick={() => moveRule(i, 1)} />
                    <IconButton type="button" variant="ghost" icon="trash" label={`Delete rule ${i + 1}`} onClick={() => setMedia({ ...media, exceptions: media.exceptions.filter((_, n) => n !== i) })} />
                  </span>
                  {rule.category === null && rule.originalLanguage === null && (
                    <p className="basis-full pl-7 text-meta text-warn">Pick a category or an original language, otherwise the rule matches everything.</p>
                  )}
                </Well>
              </li>
            ))}
          </ol>
        )}
      </Section>

      <Section title="Category defaults" description="Used when you add a title; a title can pick its own on its page. Subtitles don't change the audio language.">
        <div className="grid gap-4 md:grid-cols-3">
          {categories.map(({ key, label }) => (
            <Well key={key} className="space-y-3">
              <p className="font-semibold">{label}</p>
              <Field label="Quality profile">
                <Select value={media[key].profileId} onChange={(e) => setMedia({ ...media, [key]: { ...media[key], profileId: Number(e.target.value) } })}>
                  {options.profiles.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                </Select>
              </Field>
              <Field label="Version">
                <Select value={media[key].version ?? ""} onChange={(e) => setMedia({ ...media, [key]: { ...media[key], version: e.target.value || null } })}>
                  <option value="">Use default ({media.defaultVersion})</option>
                  {versions.map((v) => <option key={v} value={v}>{versionLabel(v)}</option>)}
                </Select>
              </Field>
              <PickList label="Subtitles" values={media[key].subtitles} choices={languages} empty="None wanted"
                onChange={(subtitles) => setMedia({ ...media, [key]: { ...media[key], subtitles } })} />
            </Well>
          ))}
        </div>
      </Section>

      <Section title="When to fetch subtitles" description="Wanted subtitles are fetched after each import and retried every 6 hours while one is missing.">
        <div className="space-y-4">
          <Switch checked={subs.onlyWhenAudioDiffers} onChange={(onlyWhenAudioDiffers) => setSubs({ ...subs, onlyWhenAudioDiffers })}
            label="Only when the audio is in another language" description="No French subtitles for a file that already has French audio." />
          <Switch checked={subs.preferHearingImpaired} onChange={(preferHearingImpaired) => setSubs({ ...subs, preferHearingImpaired })}
            label="Prefer hearing-impaired subtitles" />
        </div>
      </Section>

      <Section title="OpenSubtitles.com" description="Used once an API key is set. An account raises the daily download limit.">
        <div className="grid gap-4 md:grid-cols-3">
          <Field label="API key" hint="From your OpenSubtitles.com profile, API consumers.">
            <Input value={account.apiKey ?? ""} onChange={(e) => setAccount({ apiKey: e.target.value || null })} autoComplete="off" className="font-mono text-meta" />
          </Field>
          <Field label="Username">
            <Input value={account.username ?? ""} onChange={(e) => setAccount({ username: e.target.value || null })} autoComplete="off" />
          </Field>
          <Field label="Password">
            <Input type="password" value={account.password ?? ""} onChange={(e) => setAccount({ password: e.target.value || null })} autoComplete="new-password" />
          </Field>
        </div>
      </Section>

      <SaveBar busy={busy} error={error} dirty={mediaDirty || subsDirty} />
    </form>
  );
}
