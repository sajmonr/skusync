import { DatePipe } from '@angular/common';
import { Component, computed, effect, input, output, signal, untracked } from '@angular/core';
import { debounce, form, FormField } from '@angular/forms/signals';
import { IGridifyQuery } from 'gridify-client';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { ProductVariantListItem } from '../../models/product-variant-list-item';
import { buildProductVariantQuery } from '../../utilities/build-product-variant-query';

@Component({
  selector: 'app-product-variant-table',
  imports: [ButtonModule, DatePipe, FormField, InputTextModule, TableModule],
  templateUrl: './product-variant-table.html',
  styleUrl: './product-variant-table.scss',
})
export class ProductVariantTable {
  readonly items = input.required<readonly ProductVariantListItem[]>();
  readonly totalCount = input.required<number>();
  readonly loading = input.required<boolean>();
  readonly error = input<string | null>(null);
  readonly queryChange = output<IGridifyQuery>();
  readonly retryRequest = output<void>();

  protected readonly searchModel = signal({ search: '' });
  protected readonly searchForm = form(this.searchModel, (fields) => {
    debounce(fields.search, 350);
  });
  protected readonly tableItems = computed(() => [...this.items()]);
  protected readonly first = signal(0);
  protected readonly pageSize = signal(25);
  private lastLazyEvent: TableLazyLoadEvent = {
    first: 0,
    rows: 25,
    sortField: 'updatedOnUtc',
    sortOrder: -1
  };
  private searchEffectInitialized = false;

  constructor() {
    effect(() => {
      const search = this.searchModel().search;
      if (!this.searchEffectInitialized) {
        this.searchEffectInitialized = true;
        return;
      }

      untracked(() => this.requestPage({ ...this.lastLazyEvent, first: 0 }, search));
    });
  }

  protected load(event: TableLazyLoadEvent): void {
    this.requestPage(event);
  }

  protected clearSearch(): void {
    this.searchForm.search().value.set('');
  }

  protected retry(): void {
    this.retryRequest.emit();
  }

  private requestPage(event: TableLazyLoadEvent, search = this.searchModel().search): void {
    const rows = event.rows ?? this.pageSize();
    const first = event.first ?? 0;
    this.lastLazyEvent = { ...event, rows, first };
    this.first.set(first);
    this.pageSize.set(rows);
    this.queryChange.emit(
      buildProductVariantQuery({
        first,
        rows,
        search,
        sortField: event.sortField,
        sortOrder: event.sortOrder
      })
    );
  }
}
