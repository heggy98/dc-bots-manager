import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

export type AppLang = 'cs' | 'en';

export interface Translations {
    [key: string]: string;
}

const CS: Translations = {
    'nav.dashboard': 'Dashboard',
    'nav.logs': 'Systémové logy',
    'nav.config': 'Konfigurace',
    'nav.login': 'Přihlášení',
    'nav.logout': 'Odhlásit',
    'home.title': 'BotManager',
    'home.subtitle': 'Správa Discord Botů',
    'home.no_bots': 'Žádní boti nenalezeni.',
    'login.title': 'Přihlášení administrátora',
    'login.subtitle': 'Přihlaste se ke správě Discord Botů.',
    'login.email': 'E-mailová adresa',
    'login.password': 'Heslo',
    'login.signin': 'Přihlásit se',
    'login.signing_in': 'Přihlašování...',
    'login.or': 'NEBO',
    'login.google': 'Přihlásit se přes Google',
    'login.error_credentials': 'Nesprávné přihlašovací údaje',
    'login.error_locked': 'Příliš mnoho pokusů. Zkuste to později.',
    'login.error_google': 'Google autentizace selhala',
    'login.fill_fields': 'Vyplňte e-mail a heslo',
    'admin.title': 'Administrace',
    'admin.subtitle': 'Spravujte boty, sledujte výkon.',
    'admin.add_bot': '+ Přidat nového bota',
    'admin.cancel': 'Zrušit',
    'admin.register_bot': 'Registrovat nového bota',
    'admin.bot_name': 'Název bota',
    'admin.bot_token': 'Token bota',
    'admin.creating': 'Vytváření...',
    'admin.register': 'Registrovat bota',
    'admin.loading': 'Načítání botů...',
    'admin.no_bots': 'Žádní boti nenalezeni. Klikněte na "Přidat nového bota" a začněte.',
    'admin.requests_24h': 'Požadavky (24h)',
    'admin.errors_24h': 'Chyby (24h)',
    'admin.manage': 'Spravovat a konfigurovat',
    'admin.fill_fields': 'Zadejte název i token',
    'admin.create_error': 'Nepovedlo se vytvořit bota. Zkuste to znovu.',
    'bot.back': '← Zpět na Dashboard',
    'bot.start': 'Spustit bota',
    'bot.stop': 'Zastavit bota',
    'bot.restart': 'Restart',
    'bot.config': 'Konfigurace bota',
    'bot.config_desc': 'Spravujte specifická Discord Channel & Message ID',
    'bot.teams_msg': 'Teams Message ID',
    'bot.role_msg': 'Role Message ID',
    'bot.reaction_ch': 'Reaction Channel ID',
    'bot.save_config': 'Uložit konfiguraci',
    'bot.saving': 'Ukládání...',
    'bot.team_mgmt': 'Správa týmů',
    'bot.team_desc': 'Správa týmů tohoto bota.',
    'bot.manage_teams': 'Spravovat týmy',
    'bot.activity': 'Aktivita (24h)',
    'bot.total_req': 'Celkem požadavků',
    'bot.total_err': 'Celkem chyb',
    'bot.logs': 'Logy bota',
    'bot.no_logs': 'Žádné logy.',
    'bot.history': 'Historie spuštění',
    'bot.history_start': 'Spuštěno',
    'bot.history_stop': 'Zastaveno',
    'bot.history_duration': 'Délka',
    'bot.history_reason': 'Důvod',
    'bot.confirm_start': 'Opravdu chcete spustit bota?',
    'bot.confirm_stop': 'Opravdu chcete zastavit bota?',
    'bot.confirm_restart': 'Opravdu chcete restartovat bota?',
    'bot.running': 'Běží',
    'logs.title': 'Systémové logy',
    'logs.subtitle': 'Globální logy aplikace a služeb.',
    'logs.refresh': 'Obnovit logy',
    'logs.system_tab': 'Systémové logy',
    'logs.login_tab': 'Přihlášení',
    'logs.no_logs': 'Žádné systémové logy.',
    'logs.no_login_logs': 'Žádné záznamy přihlášení.',
    'config.title': 'Konfigurace systému',
    'config.subtitle': 'Nastavení Bruteforce ochrany a dalších parametrů.',
    'config.save': 'Uložit',
    'config.saved': 'Uloženo.',
    'footer.uptime': 'BotManager běží',
    'status.Online': 'Připojen',
    'status.Offline': 'Odpojen',
    'status.Working': 'Pracuje'
};

const EN: Translations = {
    'nav.dashboard': 'Dashboard',
    'nav.logs': 'System Logs',
    'nav.config': 'Configuration',
    'nav.login': 'Login',
    'nav.logout': 'Logout',
    'home.title': 'BotManager',
    'home.subtitle': 'Discord Bot Management',
    'home.no_bots': 'No bots found.',
    'login.title': 'Admin Login',
    'login.subtitle': 'Sign in to manage your Discord Bots.',
    'login.email': 'Email Address',
    'login.password': 'Password',
    'login.signin': 'Sign In',
    'login.signing_in': 'Signing in...',
    'login.or': 'OR',
    'login.google': 'Sign in with Google',
    'login.error_credentials': 'Invalid credentials',
    'login.error_locked': 'Too many attempts. Please try again later.',
    'login.error_google': 'Google authentication failed',
    'login.fill_fields': 'Please enter email and password',
    'admin.title': 'Admin Dashboard',
    'admin.subtitle': 'Manage your bots, monitor performance.',
    'admin.add_bot': '+ Add New Bot',
    'admin.cancel': 'Cancel',
    'admin.register_bot': 'Register New Bot',
    'admin.bot_name': 'Bot Name',
    'admin.bot_token': 'Bot Token',
    'admin.creating': 'Creating...',
    'admin.register': 'Register Bot',
    'admin.loading': 'Loading bots...',
    'admin.no_bots': 'No bots found. Click "Add New Bot" to get started.',
    'admin.requests_24h': 'Requests (24h)',
    'admin.errors_24h': 'Errors (24h)',
    'admin.manage': 'Manage & Configure',
    'admin.fill_fields': 'Please provide both Name and Token',
    'admin.create_error': 'Failed to create bot. Please try again.',
    'bot.back': '← Back to Dashboard',
    'bot.start': 'Start Bot',
    'bot.stop': 'Stop Bot',
    'bot.restart': 'Restart',
    'bot.config': 'Bot Configuration',
    'bot.config_desc': 'Manage specific Discord Channel & Message IDs',
    'bot.teams_msg': 'Teams Message ID',
    'bot.role_msg': 'Role Message ID',
    'bot.reaction_ch': 'Reaction Channel ID',
    'bot.save_config': 'Save Configuration',
    'bot.saving': 'Saving...',
    'bot.team_mgmt': 'Team Management',
    'bot.team_desc': 'Manage teams for this bot.',
    'bot.manage_teams': 'Manage Teams',
    'bot.activity': 'Activity (24h)',
    'bot.total_req': 'Total Requests',
    'bot.total_err': 'Total Errors',
    'bot.logs': 'Bot Logs',
    'bot.no_logs': 'No logs available.',
    'bot.history': 'Run History',
    'bot.history_start': 'Started',
    'bot.history_stop': 'Stopped',
    'bot.history_duration': 'Duration',
    'bot.history_reason': 'Reason',
    'bot.confirm_start': 'Are you sure you want to start the bot?',
    'bot.confirm_stop': 'Are you sure you want to stop the bot?',
    'bot.confirm_restart': 'Are you sure you want to restart the bot?',
    'bot.running': 'Running',
    'logs.title': 'System Logs',
    'logs.subtitle': 'Global application and service logs.',
    'logs.refresh': 'Refresh Logs',
    'logs.system_tab': 'System Logs',
    'logs.login_tab': 'Login Audit',
    'logs.no_logs': 'No system logs available.',
    'logs.no_login_logs': 'No login audit entries.',
    'config.title': 'System Configuration',
    'config.subtitle': 'Configure Bruteforce protection and other parameters.',
    'config.save': 'Save',
    'config.saved': 'Saved.',
    'footer.uptime': 'BotManager running for',
    'status.Online': 'Connected',
    'status.Offline': 'Disconnected',
    'status.Working': 'Working'
};

@Injectable({ providedIn: 'root' })
export class I18nService {
    private langSubject = new BehaviorSubject<AppLang>(this.getSavedLang());
    lang$ = this.langSubject.asObservable();

    private getSavedLang(): AppLang {
        return (localStorage.getItem('lang') as AppLang) || 'cs';
    }

    get currentLang(): AppLang { return this.langSubject.value; }

    setLang(lang: AppLang): void {
        localStorage.setItem('lang', lang);
        this.langSubject.next(lang);
    }

    t(key: string): string {
        const dict = this.currentLang === 'cs' ? CS : EN;
        return dict[key] || key;
    }
}
