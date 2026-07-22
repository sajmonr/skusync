import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    data: { pageTitle: 'Dashboard' },
    loadChildren: () =>
      import('./features/dashboard/dashboard.routes').then((route) => route.DASHBOARD_ROUTES)
  },
  {
    path: 'variants',
    data: { pageTitle: 'Product variants' },
    loadChildren: () =>
      import('./features/product-variants/product-variants.routes').then(
        (route) => route.PRODUCT_VARIANTS_ROUTES
      )
  },
  { path: '**', redirectTo: '' }
];
