"use client";

import type { ReactNode } from "react";
import { useGetProfile } from "@/api/generated/identity/identity";
import { Skeleton } from "@/components/ui/skeleton";

// What the caller may do comes from the API (canAdminister), never from a role name. A courtesy:
// the backend's admin policy is what refuses a non-admin.
export function AdminOnly({ children }: { children: ReactNode }) {
  const { data: profile } = useGetProfile();
  if (!profile) return <Skeleton className="h-32 w-full max-w-3xl" />;
  if (!profile.canAdminister) {
    return <p className="text-sm text-muted-foreground">This page is for administrators.</p>;
  }
  return children;
}
