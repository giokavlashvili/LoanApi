import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { 
  LoanApplicationClient, 
  LoanApplicationDto, 
  PaginatedListOfLoanApplicationDto,
  DeleteApplicationCommand,
  LoanStatus
} from '../../web-api-client';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-loan-applications',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './loan-applications.component.html',
  styleUrl: './loan-applications.component.css'
})
export class LoanApplicationsComponent implements OnInit {
  applications = signal<LoanApplicationDto[]>([]);
  isLoading = signal(false);
  errorMessage = signal<string | null>(null);
  currentPage = signal(1);
  pageSize = signal(10);
  totalCount = signal(0);
  totalPages = signal(0);
  hasNextPage = computed(() => this.currentPage() < this.totalPages());
  hasPreviousPage = computed(() => this.currentPage() > 1);

  constructor(
    private loanClient: LoanApplicationClient,
    private authService: AuthService,
    private router: Router
  ) {
    // AuthService already checks authentication status on initialization
  }

  ngOnInit(): void {
    this.loadApplications();
  }

  loadApplications(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.loanClient.loanApplication_GetApplications(
      this.currentPage(),
      this.pageSize(),
      null
    ).subscribe({
      next: (response: PaginatedListOfLoanApplicationDto) => {
        this.applications.set(response.items || []);
        this.currentPage.set(response.pageNumber);
        this.totalCount.set(response.totalCount);
        this.totalPages.set(response.totalPages);
        this.isLoading.set(false);
      },
      error: (error) => {
        this.errorMessage.set('Failed to load loan applications. Please try again.');
        this.isLoading.set(false);
        console.error('Error loading applications:', error);
      }
    });
  }

  deleteApplication(id: number): void {
    if (!confirm('Are you sure you want to delete this loan application?')) {
      return;
    }

    const command = new DeleteApplicationCommand();
    command.id = id;

    this.loanClient.loanApplication_DeleteApplication(command, null).subscribe({
      next: () => {
        this.loadApplications();
      },
      error: (error) => {
        alert('Failed to delete loan application. Please try again.');
        console.error('Error deleting application:', error);
      }
    });
  }

  editApplication(id: number): void {
    this.router.navigate(['/loan-applications', id, 'edit']);
  }

  createNew(): void {
    this.router.navigate(['/loan-applications', 'new']);
  }

  goToPage(page: number): void {
    if (page >= 1 && page <= this.totalPages()) {
      this.currentPage.set(page);
      this.loadApplications();
    }
  }

  getStatusClass(status: LoanStatus): string {
    const statusMap: Record<LoanStatus, string> = {
      [LoanStatus.Sent]: 'status-sent',
      [LoanStatus.InProcess]: 'status-inprocess',
      [LoanStatus.Accepted]: 'status-accepted',
      [LoanStatus.Rejected]: 'status-rejected'
    };
    return statusMap[status] || '';
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric'
    });
  }

  formatCurrency(amount: number, currency: string | undefined): string {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: currency || 'USD'
    }).format(amount);
  }
}

