import { Routes } from '@angular/router';
import { ProductVariantsStore } from './data-access/product-variants-store';

export const PRODUCT_VARIANTS_ROUTES: Routes = [
  {
    path: '',
    providers: [ProductVariantsStore],
    loadComponent: () =>
      import('./pages/product-variants-page/product-variants-page').then(
        (component) => component.ProductVariantsPage
      )
  }
];
