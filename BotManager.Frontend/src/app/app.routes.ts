import { Routes } from '@angular/router';
import { AppHomeComponent } from './components/app-home/app-home.component';
import { LoginComponent } from './components/login/login.component';
import { authGuard } from './guards/auth.guard';

export const routes: Routes = [
    { path: '', component: AppHomeComponent },
    { path: 'login', component: LoginComponent },
    {
        path: 'admin',
        canActivate: [authGuard],
        loadComponent: () => import('./components/admin-home/admin-home.component').then(m => m.AdminHomeComponent)
    },
    {
        path: 'admin/bot/:id',
        canActivate: [authGuard],
        loadComponent: () => import('./components/bot-detail/bot-detail.component').then(m => m.BotDetailComponent)
    },
    {
        path: 'admin/logs',
        canActivate: [authGuard],
        loadComponent: () => import('./components/admin-logs/admin-logs.component').then(m => m.AdminLogsComponent)
    },
    {
        path: 'admin/config',
        canActivate: [authGuard],
        loadComponent: () => import('./components/admin-config/admin-config.component').then(m => m.AdminConfigComponent)
    },
    {
        path: 'admin/commands',
        canActivate: [authGuard],
        loadComponent: () => import('./components/admin-commands/admin-commands.component').then(m => m.AdminCommandsComponent)
    },
    { path: '**', redirectTo: '' }
];
