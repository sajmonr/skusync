import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { API_BASE_PATH } from '../api/api-base-path';

export const apiCredentialsInterceptor: HttpInterceptorFn = (request, next) => {
  const apiBasePath = inject(API_BASE_PATH);

  return next(
    request.url.startsWith(apiBasePath)
      ? request.clone({ withCredentials: true })
      : request,
  );
};
