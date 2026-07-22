import { InjectionToken } from '@angular/core';

export const API_BASE_PATH = new InjectionToken<string>('API_BASE_PATH', {
  factory: () => '/api'
});
