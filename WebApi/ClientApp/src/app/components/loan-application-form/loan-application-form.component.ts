import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import {
  LoanApplicationClient,
  LoanTypeClient,
  CurrencyClient,
  CreateApplicationCommand,
  UpdateApplicationCommand,
  LoanTypeDto,
  CurrencyDto
} from '../../web-api-client';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-loan-application-form',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './loan-application-form.component.html',
  styleUrl: './loan-application-form.component.css'
})
export class LoanApplicationFormComponent implements OnInit {
  isEditMode = signal(false);
  applicationId = signal<number | null>(null);
  isLoading = signal(false);
  isSaving = signal(false);
  errorMessage = signal<string | null>(null);

  loanTypes = signal<LoanTypeDto[]>([]);
  currencies = signal<CurrencyDto[]>([]);

  formData = {
    loanTypeId: 0,
    amount: 0,
    currencyId: 0,
    periodPerMonth: 0
  };

  constructor(
    private loanClient: LoanApplicationClient,
    private loanTypeClient: LoanTypeClient,
    private currencyClient: CurrencyClient,
    private authService: AuthService,
    private route: ActivatedRoute,
    private router: Router
  ) {
    // AuthService already checks authentication status on initialization
  }

  ngOnInit(): void {
    this.loadDropdowns();

    const id = this.route.snapshot.paramMap.get('id');
    if (id && id !== 'new') {
      this.isEditMode.set(true);
      this.applicationId.set(+id);
      // In a real app, you'd load the application data here
      // For now, we'll just set edit mode
    }
  }

  loadDropdowns(): void {
    this.isLoading.set(true);

    // Load loan types and currencies in parallel
    Promise.all([
      new Promise<void>((resolve, reject) => {
        this.loanTypeClient.loanType_GetLoanTypes(null).subscribe({
          next: (types) => {
            this.loanTypes.set(types || []);
            resolve();
          },
          error: reject
        });
      }),
      new Promise<void>((resolve, reject) => {
        this.currencyClient.currency_GetCurrencies(null).subscribe({
          next: (currencies) => {
            this.currencies.set(currencies || []);
            resolve();
          },
          error: reject
        });
      })
    ]).then(() => {
      this.isLoading.set(false);
    }).catch((error) => {
      this.errorMessage.set('Failed to load form data. Please try again.');
      this.isLoading.set(false);
      console.error('Error loading dropdowns:', error);
    });
  }

  onSubmit(): void {
    if (!this.isFormValid()) {
      this.errorMessage.set('Please fill in all required fields with valid values.');
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    if (this.isEditMode() && this.applicationId()) {
      const command = new UpdateApplicationCommand();
      command.id = this.applicationId()!;
      command.loanTypeId = this.formData.loanTypeId;
      command.amount = this.formData.amount;
      command.currencyId = this.formData.currencyId;
      command.periodPerMonth = this.formData.periodPerMonth;

      this.loanClient.loanApplication_UpdateApplication(command, null).subscribe({
        next: () => {
          this.router.navigate(['/loan-applications']);
        },
        error: (error) => {
          this.errorMessage.set('Failed to update loan application. Please try again.');
          this.isSaving.set(false);
          console.error('Error updating application:', error);
        }
      });
    } else {
      const command = new CreateApplicationCommand();
      command.loanTypeId = this.formData.loanTypeId;
      command.amount = this.formData.amount;
      command.currencyId = this.formData.currencyId;
      command.periodPerMonth = this.formData.periodPerMonth;

      this.loanClient.loanApplication_CreateApplication(command, null).subscribe({
        next: () => {
          this.router.navigate(['/loan-applications']);
        },
        error: (error) => {
          this.errorMessage.set('Failed to create loan application. Please try again.');
          this.isSaving.set(false);
          console.error('Error creating application:', error);
        }
      });
    }
  }

  cancel(): void {
    this.router.navigate(['/loan-applications']);
  }

  private isFormValid(): boolean {
    return this.formData.loanTypeId > 0 &&
           this.formData.currencyId > 0 &&
           this.formData.amount > 0 &&
           this.formData.periodPerMonth > 0;
  }
}

