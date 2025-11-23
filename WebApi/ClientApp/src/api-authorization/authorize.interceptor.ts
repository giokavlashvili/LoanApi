import { Inject, Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent, HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError, map } from 'rxjs/operators';

@Injectable({
  providedIn: 'root'
})
export class AuthorizeInterceptor implements HttpInterceptor {
  loginUrl: string;

  constructor(@Inject('BASE_URL') baseUrl: string) {
    this.loginUrl = `${baseUrl}/Identity/Account/Login`;
  }

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    return next.handle(req).pipe(
      catchError(error => {
        // Handle redirects to login page (302 redirects)
        if (error instanceof HttpErrorResponse) {
          // Check if the error URL is a login redirect
          if (error.url && (error.url.startsWith(this.loginUrl) || error.url.includes('/Identity/Account/Login'))) {
            window.location.href = `${this.loginUrl}?ReturnUrl=${encodeURIComponent(window.location.pathname)}`;
            return throwError(() => error);
          }
          
          // Handle status 0 errors (CORS/network/redirect failures)
          // Status 0 typically means the request was blocked or redirected but failed
          if (error.status === 0) {
            // Check if this is an API request (relative or absolute URL)
            const isApiRequest = req.url.includes('/api/') || req.url.startsWith('/api');
            
            if (isApiRequest) {
              // Status 0 on API requests usually means authentication redirect failed
              // Redirect to login page
              window.location.href = `${this.loginUrl}?ReturnUrl=${encodeURIComponent(window.location.pathname)}`;
              return throwError(() => error);
            }
          }
          
          // Handle 401 Unauthorized - redirect to login
          if (error.status === 401) {
            window.location.href = `${this.loginUrl}?ReturnUrl=${encodeURIComponent(window.location.pathname)}`;
            return throwError(() => error);
          }
        }
        return throwError(() => error);
      }),
      // HACK: As of .NET 8 preview 5, some non-error responses still need to be redirected to login page.
      map((event: HttpEvent<any>) => {
        if (event instanceof HttpResponse && event.url && 
            (event.url.startsWith(this.loginUrl) || event.url.includes('/Identity/Account/Login'))) {
          window.location.href = `${this.loginUrl}?ReturnUrl=${encodeURIComponent(window.location.pathname)}`;
        }
        return event;
      }));
  }
}
