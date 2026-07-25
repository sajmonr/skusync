import { Routes } from '@angular/router';
import { MessageService } from 'primeng/api';
import { ItemSyncStore } from './data-access/item-sync-store';
import { ProductSyncStore } from './data-access/product-sync-store';

export const ITEM_SYNC_ROUTES: Routes = [
  {
    path: '',
    providers: [ItemSyncStore, ProductSyncStore, MessageService],
    loadComponent: () =>
      import('./pages/item-sync-page/item-sync-page').then((component) => component.ItemSyncPage),
  },
];
