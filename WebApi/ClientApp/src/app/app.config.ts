import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, HTTP_INTERCEPTORS, withInterceptorsFromDi } from '@angular/common/http';
import { AuthorizeInterceptor } from '../api-authorization/authorize.interceptor';
import { API_BASE_URL } from './web-api-client';

import { routes } from './app.routes';

export function getBaseUrl() {
  const baseUrl = document.getElementsByTagName('base')[0].href;
  // Remove trailing slash to avoid double slashes in API URLs
  return baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptorsFromDi()),
    { provide: 'BASE_URL', useFactory: getBaseUrl, deps: [] },
    { provide: API_BASE_URL, useFactory: getBaseUrl, deps: [] },
    { provide: HTTP_INTERCEPTORS, useClass: AuthorizeInterceptor, multi: true }
  ]
};
