import { Routes } from '@angular/router';
import { MessageService } from 'primeng/api';
import { ItemSyncStore } from './data-access/item-sync-store';

export const ITEM_SYNC_ROUTES: Routes = [
  {
    path: '',
    providers: [ItemSyncStore, MessageService],
    loadComponent: () =>
      import('./pages/item-sync-page/item-sync-page').then((component) => component.ItemSyncPage),
  },
];
