import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    redirectTo: '/loan-applications',
    pathMatch: 'full'
  },
  {
    path: 'loan-applications',
    loadComponent: () => import('./components/loan-applications/loan-applications.component').then(m => m.LoanApplicationsComponent)
  },
  {
    path: 'loan-applications/new',
    loadComponent: () => import('./components/loan-application-form/loan-application-form.component').then(m => m.LoanApplicationFormComponent)
  },
  {
    path: 'loan-applications/:id/edit',
    loadComponent: () => import('./components/loan-application-form/loan-application-form.component').then(m => m.LoanApplicationFormComponent)
  }
];
