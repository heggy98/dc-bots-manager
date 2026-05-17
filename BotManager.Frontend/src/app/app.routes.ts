import { Routes } from '@angular/router';
import { AppHomeComponent } from './components/app-home/app-home.component';
import { LoginComponent } from './components/login/login.component';
import { AdminHomeComponent } from './components/admin-home/admin-home.component';
import { BotDetailComponent } from './components/bot-detail/bot-detail.component';
import { AdminLogsComponent } from './components/admin-logs/admin-logs.component';
import { AdminConfigComponent } from './components/admin-config/admin-config.component';
import { AdminCommandsComponent } from './components/admin-commands/admin-commands.component';
import { authGuard } from './guards/auth.guard';

export const routes: Routes = [
    { path: '', component: AppHomeComponent },
    { path: 'login', component: LoginComponent },
    { path: 'admin', component: AdminHomeComponent, canActivate: [authGuard] },
    { path: 'admin/bot/:id', component: BotDetailComponent, canActivate: [authGuard] },
    { path: 'admin/logs', component: AdminLogsComponent, canActivate: [authGuard] },
    { path: 'admin/config', component: AdminConfigComponent, canActivate: [authGuard] },
    { path: 'admin/commands', component: AdminCommandsComponent, canActivate: [authGuard] },
    { path: '**', redirectTo: '' }
];
