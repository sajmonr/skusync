export type NavigationIcon = 'dashboard' | 'variants' | 'activity';

export interface NavigationItem {
  readonly label: string;
  readonly route: string;
  readonly icon: NavigationIcon;
  readonly enabled: boolean;
}

export const NAVIGATION_ITEMS: readonly NavigationItem[] = [
  { label: 'Dashboard', route: '/', icon: 'dashboard', enabled: true },
  { label: 'Product variants', route: '/variants', icon: 'variants', enabled: false },
  { label: 'Sync activity', route: '/activity', icon: 'activity', enabled: false }
];
