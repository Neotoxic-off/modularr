"use client";

import { useState } from "react";
import type { FormEvent } from "react";
import { api } from "@/lib/api";
import type { Category, MediaSettings as Media, VersionRule } from "@/lib/api";
import { realLanguages, reloadOptions, useOptions, versionLabel } from "@/lib/options";
import { Icon } from "@/components/icons";
import { Button, Field, SaveBar, Section, Select, Skeleton, useAction } from "@/components/ui";

export const categories: readonly { key: "movies" | "series" | "anime"; label: string }[] = [
  { key: "movies", label: "Movies" },
  { key: "series", label: "Series" },
  { key: "anime", label: "Anime" },
];

const categoryNames: Record<Category, string> = { Movie: "Movies", Series: "Series", Anime: "Anime" };

export function MediaSettings() {
  const options = useOptions();
  const [draft, setMedia] = useState<Media | null>(null);
  const media = draft ?? options?.media;
  const { busy, error, done, run } = useAction();

  if (!options || !media) return <Skeleton className="h-96 rounded-tile" />;

  const versions = options.versions;
  // Conditions match an item's real original language: Any and Original make no sense there.
  const languages = realLanguages(options).map((l) => l.name);

  const save = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => {
      setMedia(await api.saveMedia(media));
      reloadOptions();
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
      <Section title="Default version" description="The version Modularr looks for when nothing more specific applies.">
        <div className="max-w-sm">
          <Select value={media.defaultVersion} onChange={(e) => setMedia({ ...media, defaultVersion: e.target.value })} aria-label="Default version">
            {versions.map((v) => <option key={v} value={v}>{versionLabel(v)}</option>)}
          </Select>
        </div>
        <p className="mt-3 text-xs text-ink-3">“Any version” turns the check off. VO follows each title’s own original language; VF also covers VFF, TRUEFRENCH and FRENCH releases.</p>
      </Section>

      <Section title="Version exceptions" description="Checked from top to bottom; the first rule that matches a title wins over the category and default version."
        actions={<Button type="button" size="sm" onClick={() => setMedia({ ...media, exceptions: [...media.exceptions, { category: "Anime", originalLanguage: null, version: "VOSTFR" }] })}>
          <Icon name="plus" size={14} />Add rule
        </Button>}>
        {media.exceptions.length === 0 ? (
          <p className="text-sm text-ink-3">No exceptions. For example, add one so anime comes in VOSTFR.</p>
        ) : (
          <ol className="space-y-2">
            {media.exceptions.map((rule, i) => (
              <li key={i} className="flex flex-wrap items-center gap-2 well rounded-card p-2.5 text-sm">
                <span className="w-5 text-center text-xs text-ink-3 tabular-nums">{i + 1}</span>
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
                <span className="ml-auto flex gap-0.5">
                  <Button type="button" variant="ghost" size="sm" disabled={i === 0} onClick={() => moveRule(i, -1)} aria-label={`Move rule ${i + 1} up`}><Icon name="up" size={14} /></Button>
                  <Button type="button" variant="ghost" size="sm" disabled={i === media.exceptions.length - 1} onClick={() => moveRule(i, 1)} aria-label={`Move rule ${i + 1} down`}><Icon name="down" size={14} /></Button>
                  <Button type="button" variant="ghost" size="sm" onClick={() => setMedia({ ...media, exceptions: media.exceptions.filter((_, n) => n !== i) })} aria-label={`Delete rule ${i + 1}`}><Icon name="trash" size={14} /></Button>
                </span>
                {rule.category === null && rule.originalLanguage === null && (
                  <p className="basis-full pl-7 text-xs text-warn">Pick a category or an original language, otherwise the rule matches everything.</p>
                )}
              </li>
            ))}
          </ol>
        )}
      </Section>

      <Section title="Category defaults" description="Used when you add a title without choosing a profile or version. Wanted subtitles are set in Subtitles.">
        <div className="grid gap-4 md:grid-cols-3">
          {categories.map(({ key, label }) => (
            <div key={key} className="space-y-3 well rounded-card p-4">
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
            </div>
          ))}
        </div>
      </Section>

      <SaveBar busy={busy} error={error} done={done} />
    </form>
  );
}
