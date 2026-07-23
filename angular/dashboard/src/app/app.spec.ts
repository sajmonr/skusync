import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';
import { AppShell } from './layout/app-shell/app-shell';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes)]
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

});

describe('AppShell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppShell],
      providers: [provideRouter(routes)]
    }).compileComponents();
  });

  it('should render the application shell', async () => {
    const fixture = TestBed.createComponent(AppShell);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('.brand-copy strong')?.textContent).toContain('SkuSync');
    expect(compiled.querySelector('h1')?.textContent).toContain('Item sync');
    expect(compiled.querySelector('.sidebar')).toBeTruthy();
    expect(compiled.querySelector('.main-content')).toBeTruthy();
  });

  it('should collapse the desktop sidebar', () => {
    const fixture = TestBed.createComponent(AppShell);
    fixture.detectChanges();

    const toggle = fixture.nativeElement.querySelector(
      'button[aria-label="Collapse sidebar"]'
    ) as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.app-shell').classList).toContain(
      'sidebar-collapsed'
    );
  });
});
