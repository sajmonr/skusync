import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    data: { pageTitle: 'Item sync' },
    loadChildren: () =>
      import('./features/item-sync/item-sync.routes').then((route) => route.ITEM_SYNC_ROUTES),
  },
  { path: '**', redirectTo: '' },
];
