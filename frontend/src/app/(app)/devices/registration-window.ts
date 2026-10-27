"use client";

import { useEffect, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { getGetMyDevicesQueryKey, useGetProfile } from "@/api/generated/identity/identity";
import { windowState } from "@/lib/registration-window";

// While the window is open a device may register at any moment: poll, and refresh the device list
// when the window closes (a registration closes it).
const POLL_MS = 5_000;

export function useRegistrationWindow() {
  const queryClient = useQueryClient();
  const now = useNow(15_000);
  const profile = useGetProfile({
    query: {
      refetchInterval: (query) =>
        windowState(query.state.data?.registrationOpenUntil, new Date()).open ? POLL_MS : false,
    },
  });
  const state = windowState(profile.data?.registrationOpenUntil, now);

  const wasOpen = useRef(false);
  useEffect(() => {
    if (wasOpen.current && !state.open) {
      void queryClient.invalidateQueries({ queryKey: getGetMyDevicesQueryKey() });
    }
    wasOpen.current = state.open;
  }, [state.open, queryClient]);

  return { profile, state };
}

function useNow(intervalMs: number): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), intervalMs);
    return () => clearInterval(timer);
  }, [intervalMs]);
  return now;
}
