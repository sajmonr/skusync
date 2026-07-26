export type NavigationIcon = 'sync' | 'alert';

export interface NavigationItem {
  readonly label: string;
  readonly route: string;
  readonly icon: NavigationIcon;
  readonly enabled: boolean;
}

export const NAVIGATION_ITEMS: readonly NavigationItem[] = [
  { label: 'Item sync', route: '/', icon: 'sync', enabled: true },
  { label: 'Ambiguous items', route: '/ambiguous-items', icon: 'alert', enabled: true },
];
