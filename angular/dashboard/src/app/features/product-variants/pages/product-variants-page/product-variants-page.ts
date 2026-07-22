import { Component, inject } from '@angular/core';
import { IGridifyQuery } from 'gridify-client';
import { ProductVariantTable } from '../../components/product-variant-table/product-variant-table';
import { ProductVariantsStore } from '../../data-access/product-variants-store';

@Component({
  selector: 'app-product-variants-page',
  imports: [ProductVariantTable],
  templateUrl: './product-variants-page.html',
  styleUrl: './product-variants-page.scss',
})
export class ProductVariantsPage {
  protected readonly store = inject(ProductVariantsStore);

  protected load(query: IGridifyQuery): void {
    this.store.load(query);
  }

  protected retry(): void {
    this.store.retry();
  }
}
