import { Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { catchError, map, of } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private isAuthenticatedSignal = signal<boolean | null>(null);

  isAuthenticated = computed(() => {
    const authStatus = this.isAuthenticatedSignal();
    // If we haven't checked yet, assume not authenticated to be safe
    return authStatus === true;
  });

  constructor(private http: HttpClient) {
    // Check authentication status on service initialization
    this.checkAuthenticationStatus();
  }

  /**
   * Check if user is authenticated by making a request to a lightweight auth check endpoint
   * The backend will return 401 if not authenticated, which the interceptor handles
   */
  checkAuthenticationStatus(): void {
    // Use lightweight auth check endpoint instead of loading full data
    this.http.get('/api/v1/Authenticate/Check', { observe: 'response' })
      .pipe(
        map(() => true),
        catchError(() => of(false))
      )
      .subscribe(isAuth => {
        this.isAuthenticatedSignal.set(isAuth);
      });
  }

  /**
   * Navigate to login page (backend Identity page)
   */
  navigateToLogin(): void {
    window.location.href = '/Identity/Account/Login?ReturnUrl=' + encodeURIComponent(window.location.pathname);
  }

  /**
   * Navigate to logout page (backend Identity page)
   */
  logout(): void {
    // Create a form and submit it to the Identity logout endpoint
    const form = document.createElement('form');
    form.method = 'POST';
    form.action = '/Identity/Account/Logout';
    
    const returnUrlInput = document.createElement('input');
    returnUrlInput.type = 'hidden';
    returnUrlInput.name = 'returnUrl';
    returnUrlInput.value = '/';
    form.appendChild(returnUrlInput);
    
    document.body.appendChild(form);
    form.submit();
  }
}

