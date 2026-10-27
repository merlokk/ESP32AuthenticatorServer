import { Suspense } from "react";
import { connection } from "next/server";
import { auth } from "@/auth";
import { ClientOnly } from "@/components/client-only";
import { SidebarInset, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { Skeleton } from "@/components/ui/skeleton";
import { AppSidebar } from "./app-sidebar";
import { SignInRedirect } from "./sign-in-redirect";
import { UserMenu } from "./user-menu";

// The signed-in area: navigation on the left, the page on the right, the user's menu at the top
// right. The shell is static; what needs the session streams in behind the boundary.
export default function AppLayout({ children }: LayoutProps<"/">) {
  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        <header className="flex h-14 shrink-0 items-center gap-2 border-b px-4">
          <SidebarTrigger className="-ml-1" />
          <div className="flex-1" />
          <Suspense fallback={avatarSkeleton}>
            <SessionGate fallback={null}>
              <ClientOnly fallback={avatarSkeleton}>
                <UserMenu />
              </ClientOnly>
            </SessionGate>
          </Suspense>
        </header>
        <main className="flex flex-1 flex-col gap-6 p-4 md:p-8">
          <Suspense fallback={pageSkeleton}>
            <SessionGate fallback={<SignInRedirect />}>
              <ClientOnly fallback={pageSkeleton}>{children}</ClientOnly>
            </SessionGate>
          </Suspense>
        </main>
      </SidebarInset>
    </SidebarProvider>
  );
}

const avatarSkeleton = <Skeleton className="size-8 rounded-full" />;
const pageSkeleton = <Skeleton className="h-32 w-full max-w-3xl" />;

// No session, or an expired one: the redirect for people. The backend's 401 is the boundary.
async function SessionGate({
  children,
  fallback,
}: {
  children: React.ReactNode;
  fallback: React.ReactNode;
}) {
  // Auth.js draws random bytes (jose) before it reads the cookie: request time, not prerender.
  await connection();
  const session = await auth();
  return session && !session.error ? children : fallback;
}
