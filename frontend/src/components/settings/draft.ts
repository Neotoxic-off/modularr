"use client";

import { useCallback, useState } from "react";

// A form's value beside the last one loaded or saved, so the save bar shows only while they differ.
export function useDraft<T>() {
  const [saved, setSaved] = useState<T | null>(null);
  const [value, setValue] = useState<T | null>(null);
  const load = useCallback((next: T) => { setSaved(next); setValue(next); }, []);
  const dirty = value !== null && JSON.stringify(value) !== JSON.stringify(saved);
  return [value, setValue as (next: T) => void, load, dirty] as const;
}
