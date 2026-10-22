import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { AuthGate } from "@/components/AuthGate";
import { Nav } from "@/components/Nav";
import "./globals.css";

const geistSans = Geist({ variable: "--font-geist-sans", subsets: ["latin"] });
const geistMono = Geist_Mono({ variable: "--font-geist-mono", subsets: ["latin"] });

export const metadata: Metadata = {
  title: "Modularr",
  description: "Series, movies, anime and subtitles in one place.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="fr" className={`${geistSans.variable} ${geistMono.variable} antialiased`}>
      <body className="font-sans">
        <AuthGate>
          <div className="flex min-h-screen">
            <Nav />
            <main className="min-w-0 flex-1 px-4 pt-6 pb-24 md:px-10 md:pt-10 md:pb-10">{children}</main>
          </div>
        </AuthGate>
      </body>
    </html>
  );
}
