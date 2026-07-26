import { Component, computed, effect, input, output, signal, untracked } from '@angular/core';
import { debounce, form, FormField } from '@angular/forms/signals';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { AmbiguousItem } from '../../models/ambiguous-item';
import { AmbiguousItemsQuery } from '../../data-access/ambiguous-items-store';

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

  protected readonly filterModel = signal<{ search: string }>({ search: '' });
  protected readonly filterForm = form(this.filterModel, (fields) => {
    debounce(fields.search, 250);
  });
  protected readonly expandedItemId = signal<string | null>(null);
  protected readonly first = signal(0);
  protected readonly pageSize = signal(25);

  protected readonly tableItems = computed<AmbiguousItem[]>(() => [...this.items()]);
  private lastLazyEvent: TableLazyLoadEvent = { first: 0, rows: 25 };
  private filterEffectInitialized = false;

  constructor() {
    effect(() => {
      const { search } = this.filterModel();
      if (!this.filterEffectInitialized) {
        this.filterEffectInitialized = true;
        return;
      }

      untracked(() => this.requestPage({ ...this.lastLazyEvent, first: 0 }, search));
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

  private requestPage(event: TableLazyLoadEvent, search = this.filterModel().search): void {
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
    });
  }
}
