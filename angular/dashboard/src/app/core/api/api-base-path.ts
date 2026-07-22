import { InjectionToken } from '@angular/core';
import environment from '../../../environments/environment';

export const API_BASE_PATH = new InjectionToken<string>('API_BASE_PATH', {
  factory: () => environment.apiBaseUrl
});
