export type NavigationIcon = 'sync';

export interface NavigationItem {
  readonly label: string;
  readonly route: string;
  readonly icon: NavigationIcon;
  readonly enabled: boolean;
}

export const NAVIGATION_ITEMS: readonly NavigationItem[] = [
  { label: 'Item sync', route: '/', icon: 'sync', enabled: true },
];
