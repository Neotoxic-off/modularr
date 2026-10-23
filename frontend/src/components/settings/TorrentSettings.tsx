"use client";

import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { api, errorMessage } from "@/lib/api";
import type { AutomationSettings, TorrentSettings as Settings } from "@/lib/api";
import { Icon } from "@/components/icons";
import { useDraft } from "@/components/settings/draft";
import { Alert, Field, Input, SaveBar, Section, Skeleton, Switch, useAction } from "@/components/ui";

// TimeSpan "d.hh:mm:ss" or "hh:mm:ss" <-> whole hours.
const toHours = (span: string | null) => {
  const m = span?.match(/^(?:(\d+)\.)?(\d+):(\d+)/);
  return m ? String(Number(m[1] ?? 0) * 24 + Number(m[2]) + Math.round(Number(m[3]) / 60)) : "";
};
const fromHours = (hours: number) => `${Math.floor(hours / 24)}.${hours % 24}:00:00`;

const kib = (bytes: number) => String(Math.round(bytes / 1024));

export function TorrentSettings() {
  const [settings, setSettings, load, dirty] = useDraft<Settings>();
  const [loadError, setLoadError] = useState<string | null>(null);
  const { busy, error, run } = useAction();

  useEffect(() => {
    api.torrentSettings().then(load).catch((e: unknown) => setLoadError(errorMessage(e)));
  }, [load]);

  if (!settings) return loadError ? <Alert>{loadError}</Alert> : <Skeleton className="h-96 rounded-tile" />;

  const set = (change: Partial<Settings>) => setSettings({ ...settings, ...change });
  const int = (value: string) => Math.max(0, Math.floor(Number(value) || 0));
  const save = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => load(await api.saveTorrentSettings(settings)));
  };

  return (
    <form onSubmit={save}>
      <Section title="Torrents" description="0 means no speed limit; an empty seeding limit means none. A finished torrent stops seeding once either limit is reached.">
        <div className="space-y-5">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="Download limit (KB/s)">
              <Input type="number" min={0} value={kib(settings.maxDownloadRate)} onChange={(e) => set({ maxDownloadRate: int(e.target.value) * 1024 })} />
            </Field>
            <Field label="Upload limit (KB/s)">
              <Input type="number" min={0} value={kib(settings.maxUploadRate)} onChange={(e) => set({ maxUploadRate: int(e.target.value) * 1024 })} />
            </Field>
            <Field label="Seed ratio limit">
              <Input type="number" min={0} step="0.1" value={settings.seedRatioLimit ?? ""}
                onChange={(e) => set({ seedRatioLimit: e.target.value === "" ? null : Math.max(0, Number(e.target.value)) })} />
            </Field>
            <Field label="Seed time limit (hours)">
              <Input type="number" min={0} value={toHours(settings.seedTimeLimit)} onChange={(e) => set({ seedTimeLimit: e.target.value === "" ? null : fromHours(int(e.target.value)) })} />
            </Field>
            <Field label="Listen port" hint="Takes effect after a restart. Forward it on your router (and map it in Docker) for incoming peers.">
              <Input type="number" min={1} max={65535} value={settings.listenPort} onChange={(e) => set({ listenPort: int(e.target.value) })} />
            </Field>
          </div>

          <details className="group rounded-ctl">
            <summary className="flex min-h-11 cursor-pointer list-none items-center gap-2 text-body font-medium text-ink-2 hover:text-ink">
              <Icon name="chevronRight" size={16} className="transition group-open:rotate-90" />Advanced
            </summary>
            <div className="space-y-4 pt-2">
              <div className="grid gap-4 md:grid-cols-3">
                <Field label="Max connections">
                  <Input type="number" min={1} value={settings.maxConnections} onChange={(e) => set({ maxConnections: int(e.target.value) })} />
                </Field>
                <Field label="Stall retries" hint="A stalled download is re-announced, then failed, blocklisted and replaced by the next best release.">
                  <Input type="number" min={0} max={100} value={settings.stallRetries} onChange={(e) => set({ stallRetries: int(e.target.value) })} />
                </Field>
                <Field label="Automatic re-grabs in a row" hint="Per series or movie, so a bad result cannot loop.">
                  <Input type="number" min={0} max={100} value={settings.maxAutomaticRegrabs} onChange={(e) => set({ maxAutomaticRegrabs: int(e.target.value) })} />
                </Field>
              </div>
              <Switch checked={settings.dhtEnabled} onChange={(dhtEnabled) => set({ dhtEnabled })}
                label="DHT" description="Finds peers without a tracker; magnet links without trackers need it. Takes effect after a restart." />
              <Switch checked={settings.localPeerDiscoveryEnabled} onChange={(localPeerDiscoveryEnabled) => set({ localPeerDiscoveryEnabled })}
                label="Local peer discovery" description="Finds peers on your local network. Takes effect after a restart." />
            </div>
          </details>

          <SaveBar busy={busy} error={error} dirty={dirty} />
        </div>
      </Section>
    </form>
  );
}

const automationFields: readonly { key: keyof AutomationSettings; label: string; hint: string; max: number }[] = [
  { key: "refreshIntervalHours", label: "Refresh metadata every (hours)", hint: "New episodes, air and release days. 0 turns it off.", max: 720 },
  { key: "searchIntervalHours", label: "Search for missing items every (hours)", hint: "Missing and upgradable episodes and movies. 0 turns it off.", max: 720 },
  { key: "searchTitlesPerCycle", label: "Titles per search", hint: "The next search goes on from where this one stopped.", max: 500 },
];

export function Automation() {
  const [settings, setSettings, load, dirty] = useDraft<AutomationSettings>();
  const [loadError, setLoadError] = useState<string | null>(null);
  const { busy, error, run } = useAction();

  useEffect(() => {
    api.automation().then(load).catch((e: unknown) => setLoadError(errorMessage(e)));
  }, [load]);

  const save = (event: FormEvent) => {
    event.preventDefault();
    if (settings) void run(async () => load(await api.saveAutomation(settings)));
  };

  return (
    <Section title="Automation">
      {!settings ? (loadError ? <Alert>{loadError}</Alert> : <Skeleton className="h-32" />) : (
        <form onSubmit={save} className="space-y-5">
          <div className="grid gap-5 md:grid-cols-3">
            {automationFields.map(({ key, label, hint, max }) => (
              <Field key={key} label={label} hint={hint}>
                <Input type="number" min={key === "searchTitlesPerCycle" ? 1 : 0} max={max} step={1} required value={Number.isNaN(settings[key]) ? "" : settings[key]}
                  onChange={(e) => setSettings({ ...settings, [key]: e.target.valueAsNumber })} className="tabular-nums" />
              </Field>
            ))}
          </div>
          <SaveBar busy={busy} error={error} dirty={dirty} />
        </form>
      )}
    </Section>
  );
}
