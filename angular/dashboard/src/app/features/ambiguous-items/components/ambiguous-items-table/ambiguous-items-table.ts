import { Component, computed, effect, input, output, signal, untracked } from '@angular/core';
import { debounce, form, FormField } from '@angular/forms/signals';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { AmbiguityReason, AmbiguousItem } from '../../models/ambiguous-item';
import { AmbiguousItemsQuery } from '../../data-access/ambiguous-items-store';

type ReasonFilter = AmbiguityReason | 'all';

interface ReasonFilterOption {
  readonly label: string;
  readonly value: ReasonFilter;
}

@Component({
  selector: 'app-ambiguous-items-table',
  imports: [ButtonModule, FormField, InputTextModule, TableModule],
  templateUrl: './ambiguous-items-table.html',
  styleUrl: './ambiguous-items-table.scss',
})
export class AmbiguousItemsTable {
  readonly items = input.required<readonly AmbiguousItem[]>();
  readonly totalCount = input.required<number>();
  readonly loading = input.required<boolean>();
  readonly error = input.required<string | null>();
  readonly queryChange = output<AmbiguousItemsQuery>();
  readonly retryRequest = output<void>();

  protected readonly filterModel = signal<{ search: string; reason: ReasonFilter }>({
    search: '',
    reason: 'all',
  });
  protected readonly filterForm = form(this.filterModel, (fields) => {
    debounce(fields.search, 250);
  });
  protected readonly expandedItemId = signal<string | null>(null);
  protected readonly first = signal(0);
  protected readonly pageSize = signal(25);
  protected readonly reasonOptions: ReasonFilterOption[] = [
    { label: 'All reasons', value: 'all' },
    { label: 'Multiple listings', value: 'MultipleListings' },
    { label: 'No listings', value: 'NoListings' },
    { label: 'Not in Shopify', value: 'ListingNotInShopify' },
  ];

  protected readonly tableItems = computed<AmbiguousItem[]>(() => [...this.items()]);
  private lastLazyEvent: TableLazyLoadEvent = { first: 0, rows: 25 };
  private filterEffectInitialized = false;

  constructor() {
    effect(() => {
      const { search, reason } = this.filterModel();
      if (!this.filterEffectInitialized) {
        this.filterEffectInitialized = true;
        return;
      }

      untracked(() => this.requestPage({ ...this.lastLazyEvent, first: 0 }, search, reason));
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

  protected reasonLabel(reason: AmbiguityReason): string {
    switch (reason) {
      case 'MultipleListings':
        return 'Multiple listings';
      case 'NoListings':
        return 'No listings';
      case 'ListingNotInShopify':
        return 'Not in Shopify';
    }
  }

  private requestPage(
    event: TableLazyLoadEvent,
    search = this.filterModel().search,
    reason = this.filterModel().reason,
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
      reason,
    });
  }
}
