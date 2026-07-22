import { Component, HostListener, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { NAVIGATION_ITEMS } from '../navigation/navigation-items';

@Component({
  selector: 'app-shell',
  imports: [ButtonModule, NgTemplateOutlet, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss'
})
export class AppShell {
  protected readonly sidebarCollapsed = signal(false);
  protected readonly mobileNavigationOpen = signal(false);

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
