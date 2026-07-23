import { Component, computed, effect, input, output, signal, untracked } from '@angular/core';
import { debounce, form, FormField } from '@angular/forms/signals';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import {
  ItemPropertyComparison,
  ItemSyncListItem,
  ItemSyncStatus,
} from '../../models/item-sync-list-item';
import { buildItemPropertyComparisons } from '../../utilities/build-item-property-comparisons';
import { deriveItemSyncStatus } from '../../utilities/derive-item-sync-status';
import { ItemSyncQuery } from '../../data-access/item-sync-store';

interface ItemSyncTableRow extends ItemSyncListItem {
  readonly comparisons: readonly ItemPropertyComparison[];
  readonly status: ItemSyncStatus;
}

type ItemSyncStatusFilter = ItemSyncStatus | 'all';

interface StatusFilterOption {
  readonly label: string;
  readonly value: ItemSyncStatusFilter;
}

@Component({
  selector: 'app-item-sync-table',
  imports: [ButtonModule, FormField, InputTextModule, TableModule],
  templateUrl: './item-sync-table.html',
  styleUrl: './item-sync-table.scss',
})
export class ItemSyncTable {
  readonly items = input.required<readonly ItemSyncListItem[]>();
  readonly totalCount = input.required<number>();
  readonly loading = input.required<boolean>();
  readonly error = input.required<string | null>();
  readonly queryChange = output<ItemSyncQuery>();
  readonly retryRequest = output<void>();
  protected readonly filterModel = signal<{ search: string; status: ItemSyncStatusFilter }>({
    search: '',
    status: 'all',
  });
  protected readonly filterForm = form(this.filterModel, (fields) => {
    debounce(fields.search, 250);
  });
  protected readonly expandedItemId = signal<string | null>(null);
  protected readonly first = signal(0);
  protected readonly pageSize = signal(25);
  protected readonly statusOptions: StatusFilterOption[] = [
    { label: 'All statuses', value: 'all' },
    { label: 'In sync', value: 'in-sync' },
    { label: 'Out of sync', value: 'out-of-sync' },
    { label: 'Missing in Skulabs', value: 'missing-in-skulabs' },
    { label: 'Pending sync', value: 'pending-sync' },
  ];
  protected readonly tableItems = computed<ItemSyncTableRow[]>(() =>
    this.items()
      .map((item) => ({
        ...item,
        comparisons: buildItemPropertyComparisons(item),
        status: deriveItemSyncStatus(item),
      })),
  );
  private lastLazyEvent: TableLazyLoadEvent = { first: 0, rows: 25 };
  private filterEffectInitialized = false;

  constructor() {
    effect(() => {
      const { search, status } = this.filterModel();
      if (!this.filterEffectInitialized) {
        this.filterEffectInitialized = true;
        return;
      }

      untracked(() => this.requestPage({ ...this.lastLazyEvent, first: 0 }, search, status));
    });
  }

  protected load(event: TableLazyLoadEvent): void {
    this.requestPage(event);
  }

  protected toggleItem(itemId: string): void {
    this.expandedItemId.update((expandedItemId) => (expandedItemId === itemId ? null : itemId));
  }

  protected isExpanded(itemId: string): boolean {
    return this.expandedItemId() === itemId;
  }

  private requestPage(
    event: TableLazyLoadEvent,
    search = this.filterModel().search,
    status = this.filterModel().status,
  ): void {
    const rows = event.rows ?? this.pageSize();
    const first = event.first ?? 0;
    this.lastLazyEvent = { ...event, rows, first };
    this.first.set(first);
    this.pageSize.set(rows);
    this.expandedItemId.set(null);
    this.queryChange.emit({
      page: Math.floor(first / rows) + 1,
      pageSize: rows,
      search: search.trim(),
      status,
    });
  }

  protected statusLabel(status: ItemSyncStatus): string {
    switch (status) {
      case 'in-sync':
        return 'In sync';
      case 'out-of-sync':
        return 'Out of sync';
      case 'missing-in-skulabs':
        return 'Missing in Skulabs';
      case 'pending-sync':
        return 'Pending sync';
    }
  }
}
