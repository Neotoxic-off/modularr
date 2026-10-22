"use client";

import { useSearchParams } from "next/navigation";
import { Suspense } from "react";
import { TitleView } from "@/components/TitleView";
import { Empty } from "@/components/ui";

// Static export has no dynamic segments, so the title comes from ?type=movie|series&id=N[&season=N].
function Title() {
  const params = useSearchParams();
  const type = params.get("type");
  const id = Number(params.get("id"));
  const season = params.get("season");
  if ((type !== "movie" && type !== "series") || !Number.isInteger(id) || id <= 0) {
    return <Empty icon="search" title="This link does not point to a title" />;
  }
  return <TitleView key={`${type}${id}`} type={type} id={id} season={season === null || season === "" ? null : Number(season) || 0} />;
}

export default function TitlePage() {
  return <Suspense><Title /></Suspense>;
}
