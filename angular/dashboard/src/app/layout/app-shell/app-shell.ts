import { Component, HostListener, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { filter, map, startWith } from 'rxjs';
import { NAVIGATION_ITEMS } from '../navigation/navigation-items';

@Component({
  selector: 'app-shell',
  imports: [ButtonModule, NgTemplateOutlet, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss'
})
export class AppShell {
  private readonly router = inject(Router);

  protected readonly sidebarCollapsed = signal(false);
  protected readonly mobileNavigationOpen = signal(false);
  protected readonly navigationItems = NAVIGATION_ITEMS;
  protected readonly pageTitle = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      startWith(null),
      map(() => {
        let route = this.router.routerState.snapshot.root;
        while (route.firstChild) {
          route = route.firstChild;
        }

        return (route.data['pageTitle'] as string | undefined) ?? 'Dashboard';
      })
    ),
    { initialValue: 'Dashboard' }
  );

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
