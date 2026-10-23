"use client";

import { NotificationHistory } from "@/components/ActivityFeed";
import { PageHeader } from "@/components/ui";

export default function HistoryPage() {
  return (
    <div className="mx-auto max-w-3xl">
      <PageHeader title="History" />
      <NotificationHistory />
    </div>
  );
}
