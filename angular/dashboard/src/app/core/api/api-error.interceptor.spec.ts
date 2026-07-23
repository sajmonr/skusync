import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiRequestError } from './api-request-error';
import { apiErrorInterceptor } from './api-error.interceptor';

describe('apiErrorInterceptor', () => {
  let httpClient: HttpClient;
  let httpTestingController: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting()
      ]
    });

    httpClient = TestBed.inject(HttpClient);
    httpTestingController = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTestingController.verify());

  it('should normalize Problem Details responses', () => {
    let receivedError: unknown;
    httpClient.get('http://localhost:5257/test').subscribe({ error: (error: unknown) => (receivedError = error) });

    httpTestingController.expectOne('http://localhost:5257/test').flush(
      {
        type: 'https://example.test/validation',
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { pageSize: ['Page size is too large.'] }
      },
      { status: 400, statusText: 'Bad Request' }
    );

    expect(receivedError).toBeInstanceOf(ApiRequestError);
    const apiError = receivedError as ApiRequestError;
    expect(apiError.problemDetails.status).toBe(400);
    expect(apiError.problemDetails['errors']).toEqual({
      pageSize: ['Page size is too large.']
    });
  });
});
