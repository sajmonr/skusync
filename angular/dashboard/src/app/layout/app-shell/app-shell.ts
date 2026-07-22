import { Component, computed, HostListener, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { catchError, map, of } from 'rxjs';
import { ApiStatusService } from '../../core/api/api-status.service';
import { NAVIGATION_ITEMS } from '../navigation/navigation-items';

type ApiConnectionState = 'checking' | 'connected' | 'unavailable';

@Component({
  selector: 'app-shell',
  imports: [ButtonModule, NgTemplateOutlet, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss'
})
export class AppShell {
  private readonly apiStatusService = inject(ApiStatusService);

  protected readonly sidebarCollapsed = signal(false);
  protected readonly mobileNavigationOpen = signal(false);
  protected readonly apiConnectionState = toSignal(
    this.apiStatusService.getStatus().pipe(
      map((): ApiConnectionState => 'connected'),
      catchError(() => of<ApiConnectionState>('unavailable'))
    ),
    { initialValue: 'checking' as ApiConnectionState }
  );
  protected readonly apiConnectionLabel = computed(() => {
    switch (this.apiConnectionState()) {
      case 'connected':
        return 'API connected';
      case 'unavailable':
        return 'API unavailable';
      default:
        return 'Checking API';
    }
  });

  protected readonly navigationItems = NAVIGATION_ITEMS;

  protected toggleSidebar(): void {
    this.sidebarCollapsed.update((collapsed) => !collapsed);
  }

  protected openMobileNavigation(): void {
    this.mobileNavigationOpen.set(true);
  }

  protected closeMobileNavigation(): void {
    this.mobileNavigationOpen.set(false);
  }

  @HostListener('document:keydown.escape')
  protected closeMobileNavigationOnEscape(): void {
    this.closeMobileNavigation();
  }
}
