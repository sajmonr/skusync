import { Component, inject, signal } from '@angular/core';
import { Router, ActivatedRoute } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { finalize } from 'rxjs';
import { AuthService } from '../../../../core/auth/auth.service';

@Component({
  selector: 'app-login-page',
  imports: [ButtonModule, InputTextModule],
  templateUrl: './login-page.html',
  styleUrl: './login-page.scss',
})
export class LoginPage {
  private readonly authService = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly password = signal('');
  protected readonly submitting = signal(false);

  protected submit(): void {
    if (!this.password() || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.authService.login(this.password()).pipe(finalize(() => this.submitting.set(false))).subscribe({
      next: () => {
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
        void this.router.navigateByUrl(returnUrl?.startsWith('/') ? returnUrl : '/');
      },
      error: () => undefined,
    });
  }
}
