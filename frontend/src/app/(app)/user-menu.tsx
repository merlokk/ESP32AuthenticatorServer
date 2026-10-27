"use client";

import { useRef } from "react";
import Link from "next/link";
import { LogOut, UserRound } from "lucide-react";
import { useGetProfile } from "@/api/generated/identity/identity";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Skeleton } from "@/components/ui/skeleton";
import { UserAvatar } from "@/components/user-avatar";

// The user's avatar (Gravatar) and their menu. From GET /api/identity/me, not the session: a new
// user name shows at once.
export function UserMenu() {
  const { data: profile } = useGetProfile();
  // POST /sign-out is a route, not a server action: it survives a deployment.
  const signOut = useRef<HTMLFormElement>(null);

  if (!profile) return <Skeleton className="size-8 rounded-full" />;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" className="rounded-full" aria-label="Account menu">
          <UserAvatar email={profile.email} userName={profile.userName} />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-56">
        <DropdownMenuLabel className="flex flex-col">
          <span className="font-medium text-foreground">{profile.userName}</span>
          <span className="text-xs font-normal text-muted-foreground">{profile.email}</span>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuGroup>
          <DropdownMenuItem asChild>
            <Link href="/profile">
              <UserRound aria-hidden />
              Profile
            </Link>
          </DropdownMenuItem>
          <DropdownMenuItem onSelect={() => signOut.current?.requestSubmit()}>
            <LogOut aria-hidden />
            Sign out
          </DropdownMenuItem>
        </DropdownMenuGroup>
      </DropdownMenuContent>
      <form ref={signOut} method="post" action="/sign-out" hidden />
    </DropdownMenu>
  );
}
