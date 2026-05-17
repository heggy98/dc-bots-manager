import { Injectable } from '@angular/core';
import { BehaviorSubject, catchError, of, tap } from 'rxjs';
import { HttpClient } from '@angular/common/http';

export type AppLang = 'cs' | 'en';

export interface Translations {
    [key: string]: string;
}

// Fallback translations (for backwards compatibility)
const CS: Translations = {
    'nav.dashboard': 'Dashboard',
    'nav.commands': 'Příkazy',
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
    'login.success': 'Přihlášení proběhlo úspěšně.',
    'admin.title': 'Administrace',
    'admin.subtitle': 'Spravujte boty, sledujte výkon.',
    'admin.add_bot': '+ Přidat nového bota',
    'admin.cancel': 'Zrušit',
    'admin.register_bot': 'Registrovat nového bota',
    'admin.bot_name': 'Název bota',
    'admin.bot_token': 'Token bota',
    'admin.public_bot': 'Veřejný bot',
    'admin.owner': 'Vlastník',
    'admin.visibility': 'Viditelnost',
    'admin.public': 'Veřejný',
    'admin.private': 'Soukromý',
    'admin.creating': 'Vytváření...',
    'admin.register': 'Registrovat bota',
    'admin.loading': 'Načítání botů...',
    'admin.no_bots': 'Žádní boti nenalezeni. Klikněte na "Přidat nového bota" a začněte.',
    'admin.requests_24h': 'Požadavky (24h)',
    'admin.errors_24h': 'Chyby (24h)',
    'admin.manage': 'Spravovat a konfigurovat',
    'admin.fill_fields': 'Zadejte název i token',
    'admin.create_error': 'Nepovedlo se vytvořit bota. Zkuste to znovu.',
    'admin.create_success': 'Bot byl úspěšně vytvořen.',
    'bot.back': '← Zpět na Dashboard',
    'bot.start': 'Spustit bota',
    'bot.stop': 'Zastavit bota',
    'bot.restart': 'Restart',
    'bot.config': 'Konfigurace bota',
    'bot.config_desc': 'Spravujte Discord Board Channel a Message ID',
    'bot.discord_name': 'Discord jméno',
    'bot.servers': 'Serverů',
    'bot.guilds': 'Servery',
    'bot.board_channel': 'Board Channel ID',
    'bot.board_msg': 'Board Message ID',
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
    'bot.clear_logs': 'Vyčistit logy',
    'bot.clear_history': 'Vyčistit historii běhů',
    'bot.confirm_clear_logs': 'Opravdu chcete smazat všechny logy tohoto bota?',
    'bot.confirm_clear_history': 'Opravdu chcete smazat celou historii běhů tohoto bota?',
    'bot.clear_logs_error': 'Nepodařilo se vyčistit logy bota.',
    'bot.clear_history_error': 'Nepodařilo se vyčistit historii běhů bota.',
    'bot.history': 'Historie spuštění',
    'bot.history_start': 'Spuštěno',
    'bot.history_stop': 'Zastaveno',
    'bot.history_duration': 'Délka',
    'bot.history_reason': 'Důvod',
    'bot.confirm_start': 'Opravdu chcete spustit bota?',
    'bot.confirm_stop': 'Opravdu chcete zastavit bota?',
    'bot.confirm_restart': 'Opravdu chcete restartovat bota?',
    'bot.start_success': 'Bot byl spuštěn.',
    'bot.stop_success': 'Bot byl zastaven.',
    'bot.start_error': 'Nepodařilo se spustit bota.',
    'bot.stop_error': 'Nepodařilo se zastavit bota.',
    'bot.properties': 'Vlastnosti',
    'bot.make_public': 'Nastavit veřejný',
    'bot.make_private': 'Nastavit soukromý',
    'bot.running': 'Běží',
    'bot.last_online': 'Naposledy online',
    'bot.boards': 'Board konfigurace',
    'bot.boards_empty': 'Žádné boards nakonfigurovány.',
    'bot.board_active': 'Aktivní',
    'bot.board_type': 'Typ',
    'bot.board_title': 'Název board',
    'bot.board_description': 'Popis (šablona)',
    'bot.board_subtitle_label': 'Podtitul – štítek',
    'bot.board_contact_label': 'Kontakt – štítek',
    'bot.board_edit_config': 'Upravit',
    'bot.board_edit_teams': 'Týmy',
    'bot.board_set_active': 'Nastavit aktivní',
    'bot.board_add': 'Přidat board',
    'bot.board_delete': 'Smazat',
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
    'config.save_error': 'Uložení selhalo.',
    'commands.title': 'Globální správa příkazů',
    'commands.subtitle': 'Upravte nastavení příkazů napříč vašimi boty.',
    'commands.refresh': 'Obnovit',
    'commands.none': 'Nebyly nalezeny žádné příkazy.',
    'commands.enabled': 'Zapnuto',
    'commands.disabled': 'Vypnuto',
    'commands.used_in_bots': 'Počet botů',
    'commands.has_differences': 'Nastavení se mezi boty liší.',
    'commands.permission': 'Úroveň oprávnění',
    'commands.description': 'Popis',
    'commands.user_hint': 'Nápověda',
    'commands.success_message': 'Úspěšná zpráva',
    'commands.permission_message': 'Zpráva při chybě oprávnění',
    'commands.error_message': 'Chybová zpráva',
    'commands.admin_only_message': 'Admin-only zpráva',
    'commands.invalid_args_message': 'Zpráva pro neplatné argumenty',
    'commands.edit': 'Upravit',
    'commands.save': 'Uložit',
    'commands.saved': 'Uloženo, upravených záznamů',
    'commands.save_error': 'Uložení selhalo.',
    'footer.uptime': 'BotManager běží',
    'status.Online': 'Připojen',
    'status.Offline': 'Odpojen',
    'status.Working': 'Pracuje',
    'status.Reconnecting': 'Znovu se připojuje',
    'status.Connecting': 'Připojuje se',
    'status.Disconnecting': 'Odpojuje se'
};

const EN: Translations = {
    'nav.dashboard': 'Dashboard',
    'nav.commands': 'Commands',
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
    'login.success': 'Signed in successfully.',
    'admin.title': 'Admin Dashboard',
    'admin.subtitle': 'Manage your bots, monitor performance.',
    'admin.add_bot': '+ Add New Bot',
    'admin.cancel': 'Cancel',
    'admin.register_bot': 'Register New Bot',
    'admin.bot_name': 'Bot Name',
    'admin.bot_token': 'Bot Token',
    'admin.public_bot': 'Public bot',
    'admin.owner': 'Owner',
    'admin.visibility': 'Visibility',
    'admin.public': 'Public',
    'admin.private': 'Private',
    'admin.creating': 'Creating...',
    'admin.register': 'Register Bot',
    'admin.loading': 'Loading bots...',
    'admin.no_bots': 'No bots found. Click "Add New Bot" to get started.',
    'admin.requests_24h': 'Requests (24h)',
    'admin.errors_24h': 'Errors (24h)',
    'admin.manage': 'Manage & Configure',
    'admin.fill_fields': 'Please provide both Name and Token',
    'admin.create_error': 'Failed to create bot. Please try again.',
    'admin.create_success': 'Bot created successfully.',
    'bot.back': '← Back to Dashboard',
    'bot.start': 'Start Bot',
    'bot.stop': 'Stop Bot',
    'bot.restart': 'Restart',
    'bot.config': 'Bot Configuration',
    'bot.config_desc': 'Manage Discord board channel and message IDs',
    'bot.discord_name': 'Discord name',
    'bot.servers': 'Servers',
    'bot.guilds': 'Servers',
    'bot.board_channel': 'Board Channel ID',
    'bot.board_msg': 'Board Message ID',
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
    'bot.clear_logs': 'Clear logs',
    'bot.clear_history': 'Clear run history',
    'bot.confirm_clear_logs': 'Do you really want to delete all logs for this bot?',
    'bot.confirm_clear_history': 'Do you really want to delete all run history for this bot?',
    'bot.clear_logs_error': 'Failed to clear bot logs.',
    'bot.clear_history_error': 'Failed to clear bot run history.',
    'bot.history': 'Run History',
    'bot.history_start': 'Started',
    'bot.history_stop': 'Stopped',
    'bot.history_duration': 'Duration',
    'bot.history_reason': 'Reason',
    'bot.confirm_start': 'Are you sure you want to start the bot?',
    'bot.confirm_stop': 'Are you sure you want to stop the bot?',
    'bot.confirm_restart': 'Are you sure you want to restart the bot?',
    'bot.start_success': 'Bot started successfully.',
    'bot.stop_success': 'Bot stopped successfully.',
    'bot.start_error': 'Failed to start bot.',
    'bot.stop_error': 'Failed to stop bot.',
    'bot.properties': 'Properties',
    'bot.make_public': 'Make Public',
    'bot.make_private': 'Make Private',
    'bot.running': 'Running',
    'bot.last_online': 'Last online',
    'bot.boards': 'Board Configurations',
    'bot.boards_empty': 'No boards configured.',
    'bot.board_active': 'Active',
    'bot.board_type': 'Type',
    'bot.board_title': 'Board Title',
    'bot.board_description': 'Description Template',
    'bot.board_subtitle_label': 'Subtitle Label',
    'bot.board_contact_label': 'Contact Label',
    'bot.board_edit_config': 'Edit',
    'bot.board_edit_teams': 'Teams',
    'bot.board_set_active': 'Set Active',
    'bot.board_add': 'Add Board',
    'bot.board_delete': 'Delete',
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
    'config.save_error': 'Save failed.',
    'commands.title': 'Global Commands Management',
    'commands.subtitle': 'Edit command settings across your bots.',
    'commands.refresh': 'Refresh',
    'commands.none': 'No commands found.',
    'commands.enabled': 'Enabled',
    'commands.disabled': 'Disabled',
    'commands.used_in_bots': 'Bots count',
    'commands.has_differences': 'Settings differ across bots.',
    'commands.permission': 'Permission level',
    'commands.description': 'Description',
    'commands.user_hint': 'User hint',
    'commands.success_message': 'Success message',
    'commands.permission_message': 'Permission message',
    'commands.error_message': 'Error message',
    'commands.admin_only_message': 'Admin-only message',
    'commands.invalid_args_message': 'Invalid arguments message',
    'commands.edit': 'Edit',
    'commands.save': 'Save',
    'commands.saved': 'Saved, updated rows',
    'commands.save_error': 'Save failed.',
    'footer.uptime': 'BotManager running for',
    'status.Online': 'Connected',
    'status.Offline': 'Disconnected',
    'status.Working': 'Working',
    'status.Reconnecting': 'Reconnecting',
    'status.Connecting': 'Connecting',
    'status.Disconnecting': 'Disconnecting'
};

@Injectable({ providedIn: 'root' })
export class I18nService {
    private langSubject = new BehaviorSubject<AppLang>(this.getSavedLang());
    lang$ = this.langSubject.asObservable();
    
    private loadedTranslations: { [key in AppLang]?: Translations } = {};

    /**
     * Reads persisted language preference from local storage.
     */
    private getSavedLang(): AppLang {
        return (localStorage.getItem('lang') as AppLang) || 'cs';
    }

    constructor(private http: HttpClient) {
        // Pre-load current language translations
        this.ensureLanguageLoaded(this.currentLang).subscribe();
    }

    /**
     * Returns currently selected language.
     */
    get currentLang(): AppLang { return this.langSubject.value; }

    /**
     * Persists and publishes active language selection.
     */
    setLang(lang: AppLang): void {
        localStorage.setItem('lang', lang);
        this.langSubject.next(lang);
    }

    /**
     * Ensures a language's translations are loaded from the JSON file.
     */
    private ensureLanguageLoaded(lang: AppLang) {
        if (this.loadedTranslations[lang]) {
            return of(null);
        }

        const filePath = `/i18n/${lang}.json`;
        return this.http.get<Translations>(filePath).pipe(
            tap(translations => {
                this.loadedTranslations[lang] = translations;
            }),
            catchError(() => {
                // Fallback to hardcoded translations if JSON load fails
                this.loadedTranslations[lang] = lang === 'cs' ? CS : EN;
                return of(null);
            })
        );
    }

    /**
     * Resolves a translation string for the active language.
     */
    t(key: string): string {
        const lang = this.currentLang;
        const dict = this.loadedTranslations[lang] || (lang === 'cs' ? CS : EN);
        return dict[key] || key;
    }
}
