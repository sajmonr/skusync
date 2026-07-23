import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { ApiRequestError } from './api-request-error';
import { ProblemDetails } from './problem-details';

export const apiErrorInterceptor: HttpInterceptorFn = (request, next) =>
  next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof ApiRequestError) {
        return throwError(() => error);
      }

      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      return throwError(() => new ApiRequestError(toProblemDetails(error), error));
    })
  );

function toProblemDetails(error: HttpErrorResponse): ProblemDetails {
  if (isProblemDetails(error.error)) {
    return error.error;
  }

  return {
    title: error.status === 0 ? 'Unable to reach the API.' : 'The API request failed.',
    status: error.status,
    detail: error.message
  };
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Partial<ProblemDetails>;
  return typeof candidate.title === 'string' && typeof candidate.status === 'number';
}
