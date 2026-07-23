import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = (_, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  return authService.checkSession().pipe(
    map((isAuthenticated) =>
      isAuthenticated
        ? true
        : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } }),
    ),
  );
};
