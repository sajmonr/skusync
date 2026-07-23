import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { catchError, map, Observable, of, tap } from 'rxjs';
import { API_BASE_PATH } from '../api/api-base-path';

interface SessionResponse {
  readonly isAuthenticated: boolean;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly httpClient = inject(HttpClient);
  private readonly apiBasePath = inject(API_BASE_PATH);
  private readonly authenticated = signal<boolean | null>(null);

  checkSession(): Observable<boolean> {
    if (this.authenticated() !== null) {
      return of(this.authenticated()!);
    }

    return this.httpClient.get<SessionResponse>(`${this.apiBasePath}/auth/session`).pipe(
      map((response) => response.isAuthenticated),
      tap((isAuthenticated) => this.authenticated.set(isAuthenticated)),
      catchError(() => {
        this.authenticated.set(false);
        return of(false);
      }),
    );
  }

  login(password: string): Observable<void> {
    return this.httpClient.post<void>(`${this.apiBasePath}/auth/login`, { password }).pipe(
      tap(() => this.authenticated.set(true)),
    );
  }

  markUnauthenticated(): void {
    this.authenticated.set(false);
  }
}
