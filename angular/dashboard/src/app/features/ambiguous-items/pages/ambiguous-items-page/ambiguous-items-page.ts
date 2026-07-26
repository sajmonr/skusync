import { Component, inject } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { AmbiguousItemsTable } from '../../components/ambiguous-items-table/ambiguous-items-table';
import { AmbiguousItemsQuery, AmbiguousItemsStore } from '../../data-access/ambiguous-items-store';

@Component({
  selector: 'app-ambiguous-items-page',
  imports: [ButtonModule, AmbiguousItemsTable],
  templateUrl: './ambiguous-items-page.html',
  styleUrl: './ambiguous-items-page.scss',
})
export class AmbiguousItemsPage {
  protected readonly store = inject(AmbiguousItemsStore);

  protected load(query: AmbiguousItemsQuery): void {
    this.store.load(query);
  }

  protected retry(): void {
    this.store.retry();
  }
}
