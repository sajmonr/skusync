import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { LoginPage } from './features/authentication/pages/login-page/login-page';
import { AppShell } from './layout/app-shell/app-shell';

export const routes: Routes = [
  {
    path: '',
    component: AppShell,
    canActivate: [authGuard],
    children: [
      {
        path: '',
        pathMatch: 'full',
        data: { pageTitle: 'Item sync' },
        loadChildren: () =>
          import('./features/item-sync/item-sync.routes').then((route) => route.ITEM_SYNC_ROUTES),
      },
    ],
  },
  { path: 'login', component: LoginPage, data: { pageTitle: 'Sign in' } },
  { path: '**', redirectTo: '' },
];
