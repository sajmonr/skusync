import { Component, inject } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { ToastModule } from 'primeng/toast';
import { ItemSyncTable } from '../../components/item-sync-table/item-sync-table';
import { ItemSyncQuery, ItemSyncStore } from '../../data-access/item-sync-store';
import { ProductSyncStore } from '../../data-access/product-sync-store';

@Component({
  selector: 'app-item-sync-page',
  imports: [ButtonModule, ToastModule, ItemSyncTable],
  templateUrl: './item-sync-page.html',
  styleUrl: './item-sync-page.scss',
})
export class ItemSyncPage {
  protected readonly store = inject(ItemSyncStore);
  protected readonly sync = inject(ProductSyncStore);

  protected load(query: ItemSyncQuery): void {
    this.store.load(query);
  }

  protected retry(): void {
    this.store.retry();
  }

  protected syncNow(): void {
    this.sync.syncNow(() => this.store.retry());
  }
}
