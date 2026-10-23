"use client";

import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { api, errorMessage } from "@/lib/api";
import type { MetadataSettings } from "@/lib/api";
import { NotificationSettings } from "@/components/settings/NotificationSettings";
import { Alert, Badge, Button, Field, IconButton, Input, SaveBar, Section, Skeleton, useAction } from "@/components/ui";

const keySources: Record<MetadataSettings["source"], { label: string; tone: "ok" | "neutral" }> = {
  Custom: { label: "Custom key", tone: "ok" },
  Environment: { label: "From MODULARR_TMDB_API_KEY", tone: "neutral" },
  None: { label: "No key: limited Explore", tone: "neutral" },
};

export function Connections() {
  return (
    <div className="space-y-6">
      <NotificationSettings />
      <Tmdb />
      <RequestApps />
    </div>
  );
}

function Tmdb() {
  const [metadata, setMetadata] = useState<MetadataSettings | null>(null);
  const [key, setKey] = useState("");
  const [test, setTest] = useState<{ busy: boolean; result: string | null; ok: boolean }>({ busy: false, result: null, ok: false });
  const [loadError, setLoadError] = useState<string | null>(null);
  const { busy, error, run } = useAction();

  const show = (m: MetadataSettings) => {
    setMetadata(m);
    setKey(m.tmdbApiKey ?? "");
    setTest({ busy: false, result: null, ok: false });
  };

  useEffect(() => {
    api.metadata().then(show).catch((e: unknown) => setLoadError(errorMessage(e)));
  }, []);

  const saveKey = (value: string) => run(async () => show(await api.saveMetadata(value.trim() || null)));

  async function testKey() {
    setTest({ busy: true, result: null, ok: false });
    try {
      const r = await api.testMetadata();
      setTest({ busy: false, result: r.ok ? "TMDB accepts the key." : r.error, ok: r.ok });
    } catch (e) {
      setTest({ busy: false, result: errorMessage(e), ok: false });
    }
  }

  return (
    <Section title="TMDB" description="A TMDB key opens the full movie catalogue in Explore, with every filter.">
      {!metadata ? (loadError ? <Alert>{loadError}</Alert> : <Skeleton className="h-20" />) : (
        <form onSubmit={(e: FormEvent) => { e.preventDefault(); void saveKey(key); }} className="space-y-4">
          <div className="flex flex-wrap items-center gap-3">
            <Badge tone={keySources[metadata.source].tone}>{keySources[metadata.source].label}</Badge>
            {metadata.effective && <span className="font-mono text-meta text-ink-2" aria-label="Key in use, masked">{metadata.effective}</span>}
            <span className="flex-1" />
            {metadata.source !== "None" && <Button type="button" size="sm" onClick={() => void testKey()} loading={test.busy}>Test</Button>}
            {metadata.source === "Custom" && (
              <Button type="button" size="sm" variant="ghost" disabled={busy} onClick={() => void saveKey("")}>Use the environment key</Button>
            )}
          </div>
          {test.result && <p role="status" className={`text-meta ${test.ok ? "text-ok" : "text-warn"}`}>{test.result}</p>}
          <Field label="Custom API key or read access token" hint="Overrides MODULARR_TMDB_API_KEY. Free from themoviedb.org, under Settings, API. Empty falls back to the environment key, then to the short built-in lists.">
            <Input type="password" value={key} onChange={(e) => setKey(e.target.value)} autoComplete="off" spellCheck={false} className="font-mono text-meta" />
          </Field>
          <p className="text-meta text-ink-3">This product uses the TMDB API but is not endorsed or certified by TMDB.</p>
          <SaveBar busy={busy} error={error} dirty={key.trim() !== (metadata.tmdbApiKey ?? "")} />
        </form>
      )}
    </Section>
  );
}

// The key Overseerr, Jellyseerr or Ombi use to add titles here as their Sonarr and Radarr; it acts as an administrator.
function RequestApps() {
  const [apiKey, setApiKey] = useState<string | null | undefined>(undefined);
  // Set by MODULARR_API_KEY: the server refuses to replace it, so the button is not offered.
  const [fromEnvironment, setFromEnvironment] = useState(false);
  const { busy, error, run } = useAction();

  useEffect(() => {
    api.apiKey().then((r) => { setApiKey(r.apiKey); setFromEnvironment(r.fromEnvironment); }).catch(() => setApiKey(null));
  }, []);

  const regenerate = () => {
    if (apiKey && !confirm("Replace the API key? Apps using the current one stop working until they get the new one.")) return;
    void run(async () => setApiKey((await api.regenerateApiKey()).apiKey));
  };

  return (
    <Section title="Request apps" description="Let Overseerr, Jellyseerr or Ombi use modularr as their Sonarr and Radarr server.">
      {apiKey === undefined ? <Skeleton className="h-20" /> : (
        <div className="space-y-4">
          {error && <Alert>{error}</Alert>}
          <Field label="API key" hint={fromEnvironment ? "Set by the deployment (MODULARR_API_KEY). Anyone with this key can manage the library."
            : "Anyone with this key can manage the library. Off until a key is generated."}>
            <div className="flex gap-2">
              <Input readOnly value={apiKey ?? ""} placeholder={fromEnvironment ? "Hidden: read it from the deployment" : "No key yet"} aria-label="API key" className="font-mono text-meta" onFocus={(e) => e.target.select()} />
              {apiKey && <IconButton type="button" icon="copy" label="Copy the API key" onClick={() => void navigator.clipboard.writeText(apiKey)} />}
              {!fromEnvironment && <Button type="button" icon="refresh" loading={busy} onClick={regenerate}>{apiKey ? "Regenerate" : "Generate"}</Button>}
            </div>
          </Field>
          <p className="text-meta text-ink-3">
            In Overseerr, add a Sonarr and a Radarr server with this server&apos;s address and port ({window.location.host}), this API key and the URL base <span className="font-mono">/</span>, then pick the quality profile and root folder it lists.
          </p>
        </div>
      )}
    </Section>
  );
}
