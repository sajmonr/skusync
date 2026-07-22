import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiStatusService } from './api-status.service';

describe('ApiStatusService', () => {
  it('should request the API status endpoint', () => {
    TestBed.configureTestingModule({
      providers: [ApiStatusService, provideHttpClient(), provideHttpClientTesting()]
    });
    const service = TestBed.inject(ApiStatusService);
    const httpTestingController = TestBed.inject(HttpTestingController);
    let status: string | undefined;

    service.getStatus().subscribe((response) => (status = response.status));

    httpTestingController.expectOne('/api/status').flush({
      status: 'ok',
      utcNow: '2026-07-22T16:00:00Z'
    });
    expect(status).toBe('ok');
    httpTestingController.verify();
  });
});
