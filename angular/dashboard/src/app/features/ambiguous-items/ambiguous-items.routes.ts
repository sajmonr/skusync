import { Routes } from '@angular/router';
import { AmbiguousItemsStore } from './data-access/ambiguous-items-store';

export const AMBIGUOUS_ITEMS_ROUTES: Routes = [
  {
    path: '',
    providers: [AmbiguousItemsStore],
    loadComponent: () =>
      import('./pages/ambiguous-items-page/ambiguous-items-page').then(
        (component) => component.AmbiguousItemsPage,
      ),
  },
];
