import { Component, computed, OnInit } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-nav-menu',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive],
  templateUrl: './nav-menu.component.html',
  styleUrl: './nav-menu.component.css'
})
export class NavMenuComponent implements OnInit {
  isAuthenticated = computed(() => this.authService.isAuthenticated());
  loginUrl = '/Identity/Account/Login?ReturnUrl=' + encodeURIComponent('/loan-applications');
  logoutUrl = '/Identity/Account/Logout?returnUrl=' + encodeURIComponent('/');

  constructor(public authService: AuthService) {}

  ngOnInit(): void {
    // AuthService already checks authentication status on initialization
  }
}

