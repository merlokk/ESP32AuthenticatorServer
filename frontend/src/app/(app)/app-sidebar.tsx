"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { Cpu, LayoutDashboard, Settings2, ShieldCheck, type LucideIcon } from "lucide-react";
import { useGetProfile } from "@/api/generated/identity/identity";
import {
  Sidebar,
  SidebarContent,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarRail,
} from "@/components/ui/sidebar";

type Section = { href: string; label: string; icon: LucideIcon };

// A section gets an entry when its page exists: no entry without a screen behind it.
const SECTIONS: Section[] = [
  { href: "/dashboard", label: "Dashboard", icon: LayoutDashboard },
  { href: "/devices", label: "Devices", icon: Cpu },
];

// For administrators (canAdminister from the API, not the role name). A courtesy: the backend's
// admin policy is the control.
const ADMIN_SECTIONS: Section[] = [{ href: "/admin/system", label: "System", icon: Settings2 }];

export function AppSidebar() {
  const pathname = usePathname();
  const { data: profile } = useGetProfile();

  return (
    <Sidebar collapsible="icon">
      <SidebarHeader>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton size="lg" asChild>
              <Link href="/dashboard">
                <ShieldCheck className="size-5!" aria-hidden />
                <span className="font-heading font-semibold">ESP32 Authenticator</span>
              </Link>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarHeader>
      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupContent>
            <Entries sections={SECTIONS} pathname={pathname} />
          </SidebarGroupContent>
        </SidebarGroup>
        {profile?.canAdminister && (
          <SidebarGroup>
            <SidebarGroupLabel>Admin</SidebarGroupLabel>
            <SidebarGroupContent>
              <Entries sections={ADMIN_SECTIONS} pathname={pathname} />
            </SidebarGroupContent>
          </SidebarGroup>
        )}
      </SidebarContent>
      <SidebarRail />
    </Sidebar>
  );
}

function Entries({ sections, pathname }: { sections: Section[]; pathname: string }) {
  return (
    <SidebarMenu>
      {sections.map(({ href, label, icon: Icon }) => (
        <SidebarMenuItem key={href}>
          <SidebarMenuButton asChild isActive={pathname === href} tooltip={label}>
            <Link href={href}>
              <Icon aria-hidden />
              <span>{label}</span>
            </Link>
          </SidebarMenuButton>
        </SidebarMenuItem>
      ))}
    </SidebarMenu>
  );
}
