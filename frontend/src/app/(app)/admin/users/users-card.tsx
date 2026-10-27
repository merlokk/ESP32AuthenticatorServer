"use client";

import { useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Ban, CircleCheck, DoorOpen, Pencil, Trash2, UserPlus } from "lucide-react";
import { errorMessage } from "@/api/errors";
import {
  closeRegistrationWindow,
  deleteUser,
  getGetUsersQueryKey,
  openRegistrationWindow,
  setBlocked,
  useGetProfile,
  useGetUsers,
} from "@/api/generated/identity/identity";
import type { UserRow } from "@/api/generated/model";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardAction, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { UserAvatar } from "@/components/user-avatar";
import { formatUntil, windowState } from "@/lib/registration-window";
import { ConfirmDialog } from "./confirm-dialog";
import { EditNameDialog } from "./edit-name-dialog";
import { InviteDialog } from "./invite-dialog";

const dateTimeFormat = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });

type Open =
  | { kind: "invite" }
  | { kind: "edit"; user: UserRow }
  | { kind: "block"; user: UserRow }
  | { kind: "delete"; user: UserRow };

export function UsersCard() {
  const queryClient = useQueryClient();
  // Polled: a device registering closes a window, a sign-in moves "last sign-in".
  const { data, error, isError, isPending } = useGetUsers({ query: { refetchInterval: 30_000 } });
  const { data: me } = useGetProfile();
  const [open, setOpen] = useState<Open>();
  const [busy, setBusy] = useState<string>();
  const [failures, setFailures] = useState<Record<string, string>>({});

  const refresh = () => queryClient.invalidateQueries({ queryKey: getGetUsersQueryKey() });
  const close = () => setOpen(undefined);

  // A quick action on one row: its refusal (e.g. admin.last) shows on that row.
  async function act(user: UserRow, action: () => Promise<unknown>, fallback: string) {
    setBusy(user.id);
    setFailures((current) => {
      const next = { ...current };
      delete next[user.id];
      return next;
    });
    try {
      await action();
      await refresh();
    } catch (failure) {
      setFailures((current) => ({ ...current, [user.id]: errorMessage(failure, fallback) }));
    } finally {
      setBusy(undefined);
    }
  }

  const users = data?.users ?? [];

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          Users{" "}
          {data && <span className="font-normal text-muted-foreground">({users.length})</span>}
        </CardTitle>
        <CardAction>
          <Button onClick={() => setOpen({ kind: "invite" })}>
            <UserPlus aria-hidden />
            Invite user
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent>
        {isPending ? (
          <Skeleton className="h-40 w-full" />
        ) : isError ? (
          <p role="alert" className="text-sm text-destructive">
            {errorMessage(error, "The users could not be loaded.")}
          </p>
        ) : (
          <Table className="min-w-[56rem]">
            <TableHeader>
              <TableRow>
                <TableHead>User</TableHead>
                <TableHead>Role</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Devices</TableHead>
                <TableHead className="text-right">Passkeys</TableHead>
                <TableHead>Last sign-in</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {users.map((user) => {
                const self = user.id === me?.id;
                const now = new Date();
                const window = windowState(user.registrationOpenUntil, now);
                const rowBusy = busy === user.id;
                return (
                  <TableRow key={user.id}>
                    <TableCell>
                      <div className="flex items-center gap-3">
                        <UserAvatar email={user.email} userName={user.userName} size={28} />
                        <div className="min-w-0">
                          <p className="font-medium">
                            {user.userName}
                            {self && (
                              <span className="font-normal text-muted-foreground"> (you)</span>
                            )}
                          </p>
                          <p className="text-xs text-muted-foreground">{user.email}</p>
                          {failures[user.id] && (
                            <p role="alert" className="text-xs text-destructive">
                              {failures[user.id]}
                            </p>
                          )}
                        </div>
                      </div>
                    </TableCell>
                    <TableCell className="capitalize">{user.role}</TableCell>
                    <TableCell>
                      <div className="flex flex-wrap gap-1">
                        {user.blocked && <Badge variant="destructive">Blocked</Badge>}
                        {window.open && (
                          <Badge variant="secondary">
                            Registration open until {formatUntil(window.until, now)}
                          </Badge>
                        )}
                        {!user.blocked && !window.open && (
                          <span className="text-muted-foreground">Active</span>
                        )}
                      </div>
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{user.devices}</TableCell>
                    <TableCell className="text-right tabular-nums">{user.passkeys}</TableCell>
                    <TableCell>
                      {user.lastSignInAt ? (
                        dateTimeFormat.format(new Date(user.lastSignInAt))
                      ) : (
                        <span className="text-muted-foreground">never</span>
                      )}
                    </TableCell>
                    <TableCell>
                      <div className="flex justify-end gap-1">
                        <RowAction
                          label="Edit user name"
                          disabled={rowBusy}
                          onClick={() => setOpen({ kind: "edit", user })}
                        >
                          <Pencil />
                        </RowAction>
                        <DropdownMenu>
                          <Tooltip>
                            <TooltipTrigger asChild>
                              <DropdownMenuTrigger asChild>
                                <Button
                                  variant="ghost"
                                  size="icon-sm"
                                  aria-label="Device registration"
                                  disabled={rowBusy || user.blocked}
                                >
                                  <DoorOpen aria-hidden />
                                </Button>
                              </DropdownMenuTrigger>
                            </TooltipTrigger>
                            <TooltipContent>Device registration</TooltipContent>
                          </Tooltip>
                          <DropdownMenuContent align="end">
                            <DropdownMenuLabel>Let a device register</DropdownMenuLabel>
                            {[1, 24].map((hours) => (
                              <DropdownMenuItem
                                key={hours}
                                onSelect={() =>
                                  void act(
                                    user,
                                    () => openRegistrationWindow(user.id, { hours }),
                                    "Registration could not be opened.",
                                  )
                                }
                              >
                                For {hours === 1 ? "1 hour" : "24 hours"}
                              </DropdownMenuItem>
                            ))}
                            {window.open && (
                              <>
                                <DropdownMenuSeparator />
                                <DropdownMenuItem
                                  onSelect={() =>
                                    void act(
                                      user,
                                      () => closeRegistrationWindow(user.id),
                                      "Registration could not be closed.",
                                    )
                                  }
                                >
                                  Close registration
                                </DropdownMenuItem>
                              </>
                            )}
                          </DropdownMenuContent>
                        </DropdownMenu>
                        {user.blocked ? (
                          <RowAction
                            label="Unblock"
                            disabled={rowBusy}
                            onClick={() =>
                              void act(
                                user,
                                () => setBlocked(user.id, { blocked: false }),
                                "The user was not unblocked.",
                              )
                            }
                          >
                            <CircleCheck />
                          </RowAction>
                        ) : (
                          <RowAction
                            label={self ? "You cannot block yourself" : "Block"}
                            disabled={rowBusy || self}
                            onClick={() => setOpen({ kind: "block", user })}
                          >
                            <Ban />
                          </RowAction>
                        )}
                        <RowAction
                          label={self ? "You cannot delete yourself" : "Delete"}
                          disabled={rowBusy || self}
                          destructive
                          onClick={() => setOpen({ kind: "delete", user })}
                        >
                          <Trash2 />
                        </RowAction>
                      </div>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        )}
      </CardContent>

      {open?.kind === "invite" && <InviteDialog onDone={refresh} onClose={close} />}
      {open?.kind === "edit" && (
        <EditNameDialog user={open.user} onDone={refresh} onClose={close} />
      )}
      {open?.kind === "block" && (
        <ConfirmDialog
          title={`Block ${open.user.userName}?`}
          description="They are signed out at once and cannot sign in, renew a session or register a device until unblocked."
          confirm="Block"
          destructive
          onConfirm={async () => {
            await setBlocked(open.user.id, { blocked: true });
            await refresh();
          }}
          onClose={close}
        />
      )}
      {open?.kind === "delete" && (
        <ConfirmDialog
          title={`Delete ${open.user.userName}?`}
          description={`The account of ${open.user.email} goes with its ${open.user.devices} device(s) and ${open.user.passkeys} passkey(s). This cannot be undone.`}
          confirm="Delete"
          destructive
          onConfirm={async () => {
            await deleteUser(open.user.id);
            await refresh();
          }}
          onClose={close}
        />
      )}
    </Card>
  );
}

function RowAction({
  label,
  disabled,
  destructive = false,
  onClick,
  children,
}: {
  label: string;
  disabled?: boolean;
  destructive?: boolean;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        {/* A disabled button gets no pointer events: the span keeps the tooltip saying why. */}
        <span tabIndex={disabled ? 0 : -1} className="inline-flex">
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={label}
            disabled={disabled}
            onClick={onClick}
            className={destructive ? "text-destructive hover:text-destructive" : undefined}
          >
            {children}
          </Button>
        </span>
      </TooltipTrigger>
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  );
}
