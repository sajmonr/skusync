import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ToastModule } from 'primeng/toast';
import { ItemSyncTable } from '../../components/item-sync-table/item-sync-table';
import { ItemSyncQuery, ItemSyncStore } from '../../data-access/item-sync-store';
import { ProductSyncService } from '../../data-access/product-sync.service';

@Component({
  selector: 'app-item-sync-page',
  imports: [ButtonModule, ToastModule, ItemSyncTable],
  templateUrl: './item-sync-page.html',
  styleUrl: './item-sync-page.scss',
})
export class ItemSyncPage {
  protected readonly store = inject(ItemSyncStore);
  private readonly productSync = inject(ProductSyncService);
  private readonly messages = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly syncing = signal(false);

  protected load(query: ItemSyncQuery): void {
    this.store.load(query);
  }

  protected retry(): void {
    this.store.retry();
  }

  protected syncNow(): void {
    if (this.syncing()) {
      return;
    }

    this.syncing.set(true);
    this.productSync
      .startSync()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.syncing.set(false)),
      )
      .subscribe({
        // Success arrives as a value: leaving the page tears the subscription down and completes
        // without emitting, so none of this runs against a destroyed page.
        next: () => {
          this.store.retry();
          this.messages.add({
            severity: 'success',
            summary: 'Sync complete',
            detail: 'The sync finished.',
          });
        },
        error: (error: unknown) => {
          const rateLimited = error instanceof HttpErrorResponse && error.status === 429;
          this.messages.add({
            severity: 'error',
            summary: 'Sync failed',
            detail: rateLimited
              ? 'A sync was started very recently — please wait a moment before trying again.'
              : 'The sync did not complete successfully.',
          });
        },
      });
  }
}
