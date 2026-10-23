"use client";

import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { api, errorMessage } from "@/lib/api";
import type { ImportMode, ImportSettings, Paths } from "@/lib/api";
import { FolderPicker } from "@/components/FolderPicker";
import { useDraft } from "@/components/settings/draft";
import { Alert, Field, IconButton, Input, SaveBar, Section, Segmented, Skeleton, Switch, useAction } from "@/components/ui";

const pathFields: readonly { key: keyof Paths; label: string; hint?: string }[] = [
  { key: "downloads", label: "Downloads", hint: "Where torrents download before they are imported." },
  { key: "movies", label: "Movies" },
  { key: "series", label: "Series" },
  { key: "anime", label: "Anime" },
];

const importModes: readonly { value: ImportMode; label: string; hint: string }[] = [
  { value: "Move", label: "Move", hint: "Files leave the downloads folder and the torrent is removed. No space is used twice, nothing seeds." },
  { value: "Hardlink", label: "Hardlink", hint: "Files are linked into the library and the torrent keeps seeding. Across drives they are copied." },
];

export function Library() {
  return (
    <div className="space-y-6">
      <Folders />
      <Imports />
    </div>
  );
}

function Folders() {
  const [paths, setPaths, load, dirty] = useDraft<Paths>();
  const [picking, setPicking] = useState<keyof Paths | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const { busy, error, run } = useAction();

  useEffect(() => {
    api.paths().then(load).catch((e: unknown) => setLoadError(errorMessage(e)));
  }, [load]);

  const save = (event: FormEvent) => {
    event.preventDefault();
    if (paths) void run(async () => load(await api.savePaths(paths)));
  };

  return (
    <Section title="Folders" description="Paths are on the server: in Docker, inside the container.">
      {!paths ? (loadError ? <Alert>{loadError}</Alert> : <Skeleton className="h-64" />) : (
        <form onSubmit={save} className="space-y-5">
          <div className="grid gap-5 md:grid-cols-2">
            {pathFields.map(({ key, label, hint }) => (
              <Field key={key} label={label} hint={hint}>
                <div className="flex gap-2">
                  <Input value={paths[key]} onChange={(e) => setPaths({ ...paths, [key]: e.target.value })} className="font-mono text-meta" required />
                  <IconButton type="button" icon="folder" label={`Browse for the ${label.toLowerCase()} folder`} onClick={() => setPicking(key)} />
                </div>
              </Field>
            ))}
          </div>
          <SaveBar busy={busy} error={error} dirty={dirty} />
        </form>
      )}
      {paths && picking && (
        <FolderPicker start={paths[picking]} onClose={() => setPicking(null)}
          onPick={(path) => { setPaths({ ...paths, [picking]: path }); setPicking(null); }} />
      )}
    </Section>
  );
}

function Imports() {
  const [imports, setImports, load, dirty] = useDraft<ImportSettings>();
  const [loadError, setLoadError] = useState<string | null>(null);
  const { busy, error, run } = useAction();

  useEffect(() => {
    api.importSettings().then(load).catch((e: unknown) => setLoadError(errorMessage(e)));
  }, [load]);

  const save = (event: FormEvent) => {
    event.preventDefault();
    if (imports) void run(async () => load(await api.saveImportSettings({ ...imports, recycleBinPath: imports.recycleBinPath.trim() })));
  };

  return (
    <Section title="After a download">
      {!imports ? (loadError ? <Alert>{loadError}</Alert> : <Skeleton className="h-20" />) : (
        <form onSubmit={save} className="space-y-3">
          <Segmented label="Import mode" value={imports.importMode} onChange={(importMode: ImportMode) => setImports({ ...imports, importMode })} options={importModes} />
          <p className="text-meta text-ink-3">{importModes.find((m) => m.value === imports.importMode)?.hint}</p>
          <Switch checked={imports.renameFiles} onChange={(renameFiles) => setImports({ ...imports, renameFiles })} label="Rename files"
            description="Name imported files “Series - S01E01 - Title [Quality]” or “Movie (Year) [Quality]” instead of the release name." />
          <Field label="Recycle bin" hint="Files replaced by a better release are moved here. Empty deletes them.">
            <Input value={imports.recycleBinPath} onChange={(e) => setImports({ ...imports, recycleBinPath: e.target.value })}
              placeholder="Delete replaced files" spellCheck={false} className="font-mono text-meta" />
          </Field>
          <SaveBar busy={busy} error={error} dirty={dirty} />
        </form>
      )}
    </Section>
  );
}
