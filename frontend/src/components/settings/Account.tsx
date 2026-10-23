"use client";

import { useRef } from "react";
import { api } from "@/lib/api";
import type { User } from "@/lib/api";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { Alert, Avatar, Button, Section, useAction } from "@/components/ui";

// Square-cropped and scaled down in the browser, so any photo fits the server's size limit.
async function squarePicture(file: File, size = 256): Promise<Blob> {
  const image = await createImageBitmap(file);
  const side = Math.min(image.width, image.height);
  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = size;
  canvas.getContext("2d")!.drawImage(image, (image.width - side) / 2, (image.height - side) / 2, side, side, 0, 0, size, size);
  image.close();
  return new Promise((resolve, reject) =>
    canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error("This image can’t be read."))), "image/webp", 0.9));
}

export function Account() {
  const { user, managed, setUser } = useSession();
  return (
    <div className="space-y-10">
      <Section title="Profile" description={`Signed in as ${user.username}, ${user.role === "Admin" ? "an admin" : "a member"}.`}>
        <div className="max-w-xl space-y-6">
          <p className="text-body text-ink-2">
            Your account is managed in linkarr.{" "}
            {managed?.url && <a href={managed.url} target="_blank" rel="noopener noreferrer" className="font-medium text-ink hover:underline">Open {managed.name}</a>}
          </p>
          <Picture user={user} onChange={setUser} />
        </div>
      </Section>
      <Section title="Devices" description="Every browser where you signed in stays signed in for 30 days.">
        <OtherDevices />
      </Section>
    </div>
  );
}

function Picture({ user, onChange }: { user: User; onChange: (user: User) => void }) {
  const { busy, error, run } = useAction();
  const input = useRef<HTMLInputElement>(null);

  const upload = (file: File | undefined) => file && void run(async () => onChange(await api.uploadAvatar(await squarePicture(file))));

  return (
    <>
      <div className="flex items-center gap-4">
        <Avatar user={user} className="size-20 text-3xl" />
        <div className="space-y-2">
          <div className="flex flex-wrap gap-2">
            <Button icon="plus" loading={busy} onClick={() => input.current?.click()}>{user.avatar ? "Change picture" : "Add a picture"}</Button>
            {user.avatar && <Button variant="ghost" disabled={busy} onClick={() => void run(async () => onChange(await api.removeAvatar()))}>Remove</Button>}
          </div>
          <p className="text-meta text-ink-3">PNG, JPEG, GIF or WebP. Cropped to a square.</p>
          <input ref={input} type="file" accept="image/png,image/jpeg,image/gif,image/webp" hidden
            onChange={(e) => { upload(e.target.files?.[0]); e.target.value = ""; }} />
        </div>
      </div>
      {error && <Alert>{error}</Alert>}
    </>
  );
}

function OtherDevices() {
  const { busy, error, done, run } = useAction();
  return (
    <div className="max-w-xl space-y-3">
      <div className="flex flex-wrap items-center gap-3">
        <Button icon="logout" loading={busy} onClick={() => void run(api.endOtherSessions)}>Sign out other devices</Button>
        {done && <span className="flex items-center gap-1 text-meta text-ok"><Icon name="check" size={14} />Other devices signed out</span>}
      </div>
      {error && <Alert>{error}</Alert>}
    </div>
  );
}
