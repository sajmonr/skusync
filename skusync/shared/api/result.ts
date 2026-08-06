/**
 * Why a request failed. Each reason maps to its own message, so a variant that simply hasn't synced
 * yet never reads as a broken extension.
 */
export const FailureReason = {
  /** No session token, or the API rejected the one we sent. */
  Unauthenticated: "unauthenticated",
  /** The API has nothing to show. Deliberately says no more than that. */
  NotFound: "not-found",
  /** The API could not be reached at all — usually a wrong base URL or an untrusted certificate. */
  Unreachable: "unreachable",
  /** Anything else, including 5xx. */
  Unexpected: "unexpected",
} as const;

export type FailureReason = (typeof FailureReason)[keyof typeof FailureReason];

export interface ApiSuccess<T> {
  ok: true;
  data: T;
}

export interface ApiFailure {
  ok: false;
  reason: FailureReason;
  /** Extra context for the merchant-facing message, such as the URL that didn't respond. */
  detail?: string;
}

/**
 * Discriminated on `ok`, so narrowing gives access to `data` or to `reason` but never both — the
 * compiler rejects rendering a success body for a failed lookup.
 */
export type ApiResult<T> = ApiSuccess<T> | ApiFailure;

/** A failure with no more to say than that there is nothing to display. */
export const notFound: ApiFailure = { ok: false, reason: FailureReason.NotFound };
