"use client";

import { useEffect, useState } from "react";
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import { gravatarUrl, initials } from "@/lib/gravatar";
import { cn } from "@/lib/utils";

/** The Gravatar of an address, the user name's initials until it loads (or without one). */
export function UserAvatar({
  email,
  userName,
  size = 32,
  className,
}: {
  email: string;
  userName: string;
  size?: number;
  className?: string;
}) {
  const avatar = useGravatar(email, size * 2);
  return (
    <Avatar className={cn(className)} style={{ width: size, height: size }}>
      {avatar && <AvatarImage src={avatar} alt="" />}
      <AvatarFallback className={size < 28 ? "text-[0.6rem]" : undefined}>
        {initials(userName)}
      </AvatarFallback>
    </Avatar>
  );
}

function useGravatar(email: string, size: number): string | undefined {
  const [url, setUrl] = useState<{ key: string; url: string }>();
  const key = `${email}|${size}`;
  useEffect(() => {
    let current = true;
    void gravatarUrl(email, size).then((value) => current && setUrl({ key, url: value }));
    return () => {
      current = false;
    };
  }, [email, size, key]);
  return url && url.key === key ? url.url : undefined;
}
