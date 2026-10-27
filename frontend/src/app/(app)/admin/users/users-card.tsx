"use client";

import { Fragment, useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import {
  Ban,
  ChevronDown,
  ChevronRight,
  CircleCheck,
  DoorOpen,
  Pencil,
  Trash2,
  UserPlus,
} from "lucide-react";
import { errorMessage } from "@/api/errors";
import {
  changeRole,
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
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
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
import { UserDevices } from "./user-devices";

const dateTimeFormat = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });

type Open =
  | { kind: "invite" }
  | { kind: "edit"; user: UserRow }
  | { kind: "block"; user: UserRow }
  | { kind: "delete"; user: UserRow }
  | { kind: "role"; user: UserRow; role: Role };

// The two roles (use-cases.md: a column, no role tables).
const ROLES = ["admin", "user"] as const;
type Role = (typeof ROLES)[number];
const ROLE_LABEL: Record<Role, string> = { admin: "Admin", user: "User" };

export function UsersCard() {
  const queryClient = useQueryClient();
  // Polled: a device registering closes a window, a sign-in moves "last sign-in".
  const { data, error, isError, isPending } = useGetUsers({ query: { refetchInterval: 30_000 } });
  const { data: me } = useGetProfile();
  const [open, setOpen] = useState<Open>();
  const [busy, setBusy] = useState<string>();
  // Rows whose devices are shown; each loads its list when it opens.
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set());
  const toggle = (id: string) =>
    setExpanded((current) => {
      const next = new Set(current);
      if (!next.delete(id)) next.add(id);
      return next;
    });
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
                  <Fragment key={user.id}>
                    <TableRow>
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
                      <TableCell>
                        {self ? (
                          <Tooltip>
                            <TooltipTrigger asChild>
                              <span tabIndex={0} className="capitalize">
                                {user.role}
                              </span>
                            </TooltipTrigger>
                            <TooltipContent>You cannot change your own role</TooltipContent>
                          </Tooltip>
                        ) : (
                          <DropdownMenu>
                            <DropdownMenuTrigger asChild>
                              <Button
                                variant="ghost"
                                size="sm"
                                className="-ml-2.5 capitalize"
                                aria-label={`Role of ${user.userName}: ${user.role}`}
                                disabled={rowBusy}
                              >
                                {user.role}
                                <ChevronDown aria-hidden />
                              </Button>
                            </DropdownMenuTrigger>
                            <DropdownMenuContent align="start">
                              <DropdownMenuLabel>Role</DropdownMenuLabel>
                              <DropdownMenuRadioGroup
                                value={user.role}
                                onValueChange={(role) =>
                                  role !== user.role &&
                                  setOpen({ kind: "role", user, role: role as Role })
                                }
                              >
                                {ROLES.map((role) => (
                                  <DropdownMenuRadioItem key={role} value={role}>
                                    {ROLE_LABEL[role]}
                                  </DropdownMenuRadioItem>
                                ))}
                              </DropdownMenuRadioGroup>
                            </DropdownMenuContent>
                          </DropdownMenu>
                        )}
                      </TableCell>
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
                      <TableCell className="text-right tabular-nums">
                        {user.devices === 0 ? (
                          <span className="pr-2.5">0</span>
                        ) : (
                          <Button
                            variant="ghost"
                            size="sm"
                            className="tabular-nums"
                            aria-expanded={expanded.has(user.id)}
                            aria-controls={`devices-${user.id}`}
                            aria-label={`${expanded.has(user.id) ? "Hide" : "Show"} the ${user.devices} device(s) of ${user.userName}`}
                            onClick={() => toggle(user.id)}
                          >
                            {expanded.has(user.id) ? (
                              <ChevronDown aria-hidden />
                            ) : (
                              <ChevronRight aria-hidden />
                            )}
                            {user.devices}
                          </Button>
                        )}
                      </TableCell>
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
                    {expanded.has(user.id) && user.devices > 0 && (
                      <TableRow id={`devices-${user.id}`} className="bg-muted/40 hover:bg-muted/40">
                        <TableCell colSpan={7} className="pl-14">
                          <UserDevices user={user} />
                        </TableCell>
                      </TableRow>
                    )}
                  </Fragment>
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
      {open?.kind === "role" && (
        <ConfirmDialog
          title={
            open.role === "admin"
              ? `Make ${open.user.userName} an admin?`
              : `Make ${open.user.userName} a user?`
          }
          description={
            open.role === "admin"
              ? "They can then manage users and see the system. It takes effect at their next token renewal or sign-in."
              : "They lose admin rights at once: their sessions end and they sign in again as a user."
          }
          confirm={open.role === "admin" ? "Make admin" : "Make user"}
          destructive={open.role !== "admin"}
          onConfirm={async () => {
            await changeRole(open.user.id, { role: open.role });
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
