import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import Aura from '@primeuix/themes/aura';
import { providePrimeNG } from 'primeng/config';
import { routes } from './app.routes';
import { apiErrorInterceptor } from './core/api/api-error.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([apiErrorInterceptor])),
    provideRouter(routes),
    providePrimeNG({
      license: 'eyJpZCI6IjAyYWU3MDE2LTBkOWMtNDUyNC1iNjE1LWE2ZmNiNTE0MThiMCIsInByb2R1Y3QiOiJwcmltZXVpIiwidGllciI6ImNvbW11bml0eSIsInR5cGUiOiJkZXYiLCJpYXQiOjE3ODQ3NDMwNzgsImV4cCI6MTgxNjI3OTA3OH0.tSZBCewJQAopBjzBSNBT-0WqCzKvMNR8sAUoH5YZlAsjwHktOaArlNX1fkJ7c2pFQyeL6FbORmAv2gbOcOJ5DA',
      theme: {
        preset: Aura,
        options: {
          darkModeSelector: 'system'
        }
      }
    })
  ]
};
