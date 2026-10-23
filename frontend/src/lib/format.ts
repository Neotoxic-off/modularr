// The UI is in English, so dates are too, whatever the browser's locale.
export const locale = "en";

type When = Date | string;

const dayFormat = new Intl.DateTimeFormat(locale, { day: "numeric", month: "short", year: "numeric" });
const dayTimeFormat = new Intl.DateTimeFormat(locale, { day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" });

// "Mar 3, 2026"
export const day = (when: When) => dayFormat.format(new Date(when));

// "Mar 3, 2026, 09:41 PM"
export const dayTime = (when: When) => dayTimeFormat.format(new Date(when));

// "Monday", or "Mon" and "M" for the shorter styles.
export const weekday = (when: When, style: "long" | "short" | "narrow" = "long") => new Date(when).toLocaleDateString(locale, { weekday: style });

const relative = {
  long: new Intl.RelativeTimeFormat(locale, { numeric: "auto" }),
  short: new Intl.RelativeTimeFormat(locale, { numeric: "auto", style: "narrow" }),
};

// "5 minutes ago", "yesterday", "in 2 days"
export function ago(iso: string, style: keyof typeof relative = "long"): string {
  const format = relative[style];
  const minutes = Math.round((new Date(iso).getTime() - Date.now()) / 60000);
  if (Math.abs(minutes) < 60) return format.format(minutes, "minute");
  if (Math.abs(minutes) < 1440) return format.format(Math.round(minutes / 60), "hour");
  return format.format(Math.round(minutes / 1440), "day");
}
