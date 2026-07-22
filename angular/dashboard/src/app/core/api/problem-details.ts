/** RFC 9457 error response returned by the API. */
export interface ProblemDetails {
  readonly type?: string;
  readonly title: string;
  readonly status: number;
  readonly detail?: string;
  readonly instance?: string;
  readonly [extension: string]: unknown;
}

/** Problem Details response used for request validation failures. */
export interface ValidationProblemDetails extends ProblemDetails {
  readonly errors: Readonly<Record<string, readonly string[]>>;
}
