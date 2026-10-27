"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { LogOut, UserRound } from "lucide-react";
import { useGetProfile } from "@/api/generated/identity/identity";
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
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
import { gravatarUrl, initials } from "@/lib/gravatar";

// The user's avatar (Gravatar) and their menu. From GET /api/identity/me, not the session: a new
// user name shows at once.
export function UserMenu() {
  const { data: profile } = useGetProfile();
  const avatar = useGravatar(profile?.email);
  // POST /sign-out is a route, not a server action: it survives a deployment.
  const signOut = useRef<HTMLFormElement>(null);

  if (!profile) return <Skeleton className="size-8 rounded-full" />;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" className="rounded-full" aria-label="Account menu">
          <Avatar>
            {avatar && <AvatarImage src={avatar} alt="" />}
            <AvatarFallback>{initials(profile.userName)}</AvatarFallback>
          </Avatar>
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

function useGravatar(email: string | undefined): string | undefined {
  const [url, setUrl] = useState<{ email: string; url: string }>();
  useEffect(() => {
    if (!email) return;
    let current = true;
    void gravatarUrl(email, 64).then((value) => current && setUrl({ email, url: value }));
    return () => {
      current = false;
    };
  }, [email]);
  return url && url.email === email ? url.url : undefined;
}
