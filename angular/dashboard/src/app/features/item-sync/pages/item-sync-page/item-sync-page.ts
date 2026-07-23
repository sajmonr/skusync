import { Component } from '@angular/core';
import { inject } from '@angular/core';
import { ItemSyncTable } from '../../components/item-sync-table/item-sync-table';
import { ItemSyncQuery, ItemSyncStore } from '../../data-access/item-sync-store';

@Component({
  selector: 'app-item-sync-page',
  imports: [ItemSyncTable],
  templateUrl: './item-sync-page.html',
  styleUrl: './item-sync-page.scss',
})
export class ItemSyncPage {
  protected readonly store = inject(ItemSyncStore);

  protected load(query: ItemSyncQuery): void {
    this.store.load(query);
  }

  protected retry(): void {
    this.store.retry();
  }
}
