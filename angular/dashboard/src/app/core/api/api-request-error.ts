import { HttpErrorResponse } from '@angular/common/http';
import { ProblemDetails } from './problem-details';

export class ApiRequestError extends Error {
  constructor(
    readonly problemDetails: ProblemDetails,
    readonly response: HttpErrorResponse
  ) {
    super(problemDetails.detail ?? problemDetails.title, { cause: response });
    this.name = 'ApiRequestError';
  }
}
