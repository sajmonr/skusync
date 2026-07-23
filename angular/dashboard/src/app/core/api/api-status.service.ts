import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_PATH } from './api-base-path';
import { ApiStatus } from './api-status';

@Injectable({ providedIn: 'root' })
export class ApiStatusService {
  private readonly httpClient = inject(HttpClient);
  private readonly apiBasePath = inject(API_BASE_PATH);

  getStatus(): Observable<ApiStatus> {
    return this.httpClient.get<ApiStatus>(`${this.apiBasePath}/status`);
  }
}
