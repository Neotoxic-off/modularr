import type { ReactNode } from "react";

export function Poster({ title, year, src, subtitle, badge, onClick, action }: {
  title: string;
  year: number | null;
  src?: string | null;
  subtitle?: string;
  badge?: ReactNode;
  onClick?: () => void;
  action?: ReactNode;
}) {
  return (
    <article className="group flex flex-col gap-2">
      <div className="relative aspect-[2/3] overflow-hidden rounded-xl bg-gradient-to-br from-raised to-surface ring-1 ring-line transition group-hover:ring-accent/50">
        {src ? (
          // Remote TVDB/TMDB artwork: next/image optimisation is unavailable in a static export.
          // eslint-disable-next-line @next/next/no-img-element
          <img src={src} alt="" loading="lazy" className="h-full w-full object-cover transition duration-300 group-hover:scale-[1.03]" />
        ) : (
          <span className="absolute inset-0 flex items-center justify-center p-4 text-center text-sm font-medium text-muted">{title}</span>
        )}
        {badge && <span className="absolute top-2 left-2">{badge}</span>}
        {onClick && <button onClick={onClick} aria-label={title} className="absolute inset-0 outline-none focus-visible:ring-2 focus-visible:ring-accent focus-visible:ring-inset" />}
        {action && (
          <div className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/90 to-transparent p-2 pt-8 opacity-100 transition sm:opacity-0 sm:group-hover:opacity-100 sm:group-focus-within:opacity-100">
            {action}
          </div>
        )}
      </div>
      <div className="min-w-0 px-0.5">
        <h3 className="truncate text-sm font-medium" title={title}>{title}</h3>
        <p className="text-xs text-muted">{subtitle ?? year}</p>
      </div>
    </article>
  );
}

export function PosterGrid({ children }: { children: ReactNode }) {
  return <div className="grid grid-cols-2 gap-x-4 gap-y-6 sm:grid-cols-3 lg:grid-cols-5 2xl:grid-cols-7">{children}</div>;
}
