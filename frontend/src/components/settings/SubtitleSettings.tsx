"use client";

import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { api, errorMessage } from "@/lib/api";
import type { SubtitleSettings as Settings } from "@/lib/api";
import { Alert, Field, Input, SaveBar, Section, Skeleton, Switch, useAction } from "@/components/ui";

// Which languages are wanted lives in Media management (per category) and on each title; this holds the rest.
export function SubtitleSettings() {
  const [settings, setSettings] = useState<Settings | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const { busy, error, done, run } = useAction();

  useEffect(() => {
    api.subtitleSettings().then(setSettings).catch((e: unknown) => setLoadError(errorMessage(e)));
  }, []);

  if (!settings) return loadError ? <Alert>{loadError}</Alert> : <Skeleton className="h-96 rounded-2xl" />;

  const account = settings.openSubtitles;
  const setAccount = (change: Partial<Settings["openSubtitles"]>) => setSettings({ ...settings, openSubtitles: { ...account, ...change } });
  const save = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => setSettings(await api.saveSubtitleSettings(settings)));
  };

  return (
    <form onSubmit={save} className="space-y-6">
      <Section title="Subtitles" description="Wanted subtitles are fetched after each import and retried every 6 hours while one is missing.">
        <div className="space-y-4">
          <Switch checked={settings.onlyWhenAudioDiffers} onChange={(onlyWhenAudioDiffers) => setSettings({ ...settings, onlyWhenAudioDiffers })}
            label="Only when the audio is in another language" description="No French subtitles for a file that already has French audio." />
          <Switch checked={settings.preferHearingImpaired} onChange={(preferHearingImpaired) => setSettings({ ...settings, preferHearingImpaired })}
            label="Prefer hearing-impaired subtitles" description="Otherwise plain subtitles come first." />
        </div>
      </Section>

      <Section title="OpenSubtitles.com" description="Used once an API key is set. An account raises the daily download limit.">
        <div className="grid gap-4 md:grid-cols-3">
          <Field label="API key" hint="From your OpenSubtitles.com profile, API consumers.">
            <Input value={account.apiKey ?? ""} onChange={(e) => setAccount({ apiKey: e.target.value || null })} autoComplete="off" className="font-mono text-xs" />
          </Field>
          <Field label="Username">
            <Input value={account.username ?? ""} onChange={(e) => setAccount({ username: e.target.value || null })} autoComplete="off" />
          </Field>
          <Field label="Password">
            <Input type="password" value={account.password ?? ""} onChange={(e) => setAccount({ password: e.target.value || null })} autoComplete="new-password" />
          </Field>
        </div>
      </Section>

      <SaveBar busy={busy} error={error} done={done} />
    </form>
  );
}
