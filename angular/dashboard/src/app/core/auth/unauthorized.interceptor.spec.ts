import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { apiErrorInterceptor } from '../api/api-error.interceptor';
import { AuthService } from './auth.service';
import { unauthorizedInterceptor } from './unauthorized.interceptor';

describe('unauthorizedInterceptor', () => {
  let httpClient: HttpClient;
  let httpMock: HttpTestingController;
  const navigate = vi.fn();
  const markUnauthenticated = vi.fn();

  beforeEach(() => {
    navigate.mockReset();
    markUnauthenticated.mockReset();

    TestBed.configureTestingModule({
      providers: [
        // Mirror the app's chain order so the test proves the redirect still fires after
        // apiErrorInterceptor has wrapped the error into an ApiRequestError.
        provideHttpClient(withInterceptors([apiErrorInterceptor, unauthorizedInterceptor])),
        provideHttpClientTesting(),
        { provide: Router, useValue: { navigate } },
        { provide: AuthService, useValue: { markUnauthenticated } },
      ],
    });

    httpClient = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('redirects to login and marks unauthenticated on a 401', () => {
    httpClient.get('http://localhost:5257/item-sync').subscribe({ error: () => {} });

    httpMock
      .expectOne('http://localhost:5257/item-sync')
      .flush({ title: 'Unauthorized', status: 401 }, { status: 401, statusText: 'Unauthorized' });

    expect(markUnauthenticated).toHaveBeenCalledTimes(1);
    expect(navigate).toHaveBeenCalledWith(['/login']);
  });

  it('does not redirect when the failing request is the login endpoint', () => {
    httpClient.post('http://localhost:5257/auth/login', {}).subscribe({ error: () => {} });

    httpMock
      .expectOne('http://localhost:5257/auth/login')
      .flush({ title: 'Unauthorized', status: 401 }, { status: 401, statusText: 'Unauthorized' });

    expect(markUnauthenticated).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('does not redirect on non-401 errors', () => {
    httpClient.get('http://localhost:5257/item-sync').subscribe({ error: () => {} });

    httpMock
      .expectOne('http://localhost:5257/item-sync')
      .flush({ title: 'Server error', status: 500 }, { status: 500, statusText: 'Server Error' });

    expect(markUnauthenticated).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
  });
});
