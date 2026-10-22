"use client";

import { useState } from "react";

const fallback = "bg-[radial-gradient(55%_45%_at_12%_8%,rgb(124_92_255/.32),transparent),radial-gradient(45%_40%_at_88%_18%,rgb(255_122_69/.2),transparent),radial-gradient(60%_50%_at_60%_100%,rgb(45_212_191/.16),transparent)]";

// The page's artwork, blurred into light behind the glass; a quiet three-colour wash when there is none.
export function Ambient({ src }: { src: string | null }) {
  const [layers, setLayers] = useState<readonly (string | null)[]>([src]);
  // Keeps the previous backdrop underneath while the new one fades in.
  if (layers.at(-1) !== src) setLayers([layers.at(-1) ?? null, src]);
  return (
    <div aria-hidden="true" className="pointer-events-none fixed inset-0 -z-10 overflow-hidden bg-canvas">
      {layers.map((layer, i) => (
        <div key={`${layer}${i}`} className={`absolute inset-0 ${i > 0 ? "animate-[fade_600ms_ease-out_both]" : ""}`}>
          {layer
            // eslint-disable-next-line @next/next/no-img-element
            ? <img src={layer} alt="" className="h-full w-full scale-125 object-cover opacity-60 blur-[90px] saturate-150" />
            : <div className={`h-full w-full ${fallback}`} />}
        </div>
      ))}
      <div className="absolute inset-0 bg-[radial-gradient(120%_90%_at_50%_0%,transparent,rgb(7_7_11/.7))]" />
      <div className="absolute inset-0 bg-canvas/55" />
    </div>
  );
}
