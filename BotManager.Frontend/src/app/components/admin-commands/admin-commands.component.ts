import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CommandsService, GlobalCommandDto, UpdateGlobalCommandDto } from '../../services/commands.service';
import { I18nService } from '../../services/i18n.service';
import { CommandEditModalComponent } from './command-edit-modal.component';
import { ToastrService } from 'ngx-toastr';

interface CommandGroupVm {
  commandName: string;
  items: GlobalCommandDto[];
}

@Component({
  selector: 'app-admin-commands',
  standalone: true,
  imports: [CommonModule, FormsModule, CommandEditModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-commands.component.html',
  styleUrl: './admin-commands.component.css'
})
export class AdminCommandsComponent implements OnInit {
  readonly commands = signal<GlobalCommandDto[]>([]);
  readonly commandGroups = computed(() => this.buildCommandGroups(this.commands()));
  readonly loading = signal(true);
  readonly saveMessage = signal('');
  readonly showEditModal = signal(false);
  readonly editingCommand = signal<GlobalCommandDto | null>(null);

  /**
   * Creates a new admin commands component.
   */
  constructor(
    private commandsService: CommandsService,
    private toastr: ToastrService,
    public i18n: I18nService
  ) { }

  /**
   * Loads command data on component initialization.
   */
  ngOnInit(): void {
    this.loadCommands();
  }

  /**
   * Loads and groups global commands for UI rendering.
   */
  loadCommands(): void {
    this.loading.set(true);
    this.commandsService.getGlobalCommands().subscribe({
      next: (data) => {
        this.commands.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      }
    });
  }

  /**
   * Opens the command edit modal.
   */
  openEditModal(command: GlobalCommandDto): void {
    this.editingCommand.set(command);
    this.showEditModal.set(true);
    this.saveMessage.set('');
  }

  /**
   * Closes the command edit modal.
   */
  closeEditModal(): void {
    this.showEditModal.set(false);
    this.editingCommand.set(null);
  }

  /**
   * Saves edited command settings.
   */
  saveCommand(draft: UpdateGlobalCommandDto): void {
    const command = this.editingCommand();
    if (!command) {
      return;
    }

    this.commandsService.updateGlobalCommand(command.commandName, command.subCommandName, draft).subscribe({
      next: (result) => {
        this.saveMessage.set(`${this.i18n.t('commands.saved')}: ${result.updated}`);
        this.toastr.success(this.saveMessage(), this.i18n.t('commands.save'));
        this.loadCommands();
      },
      error: () => {
        this.saveMessage.set(this.i18n.t('commands.save_error'));
        this.toastr.error(this.saveMessage(), this.i18n.t('commands.save'));
      }
    });
  }

  /**
   * TrackBy for command groups.
   */
  trackByCommandGroup(_index: number, group: CommandGroupVm): string {
    return group.commandName;
  }

  /**
   * TrackBy for command rows.
   */
  trackByCommandKey(_index: number, item: GlobalCommandDto): string {
    return `${item.commandName}:${item.subCommandName ?? ''}`;
  }

  /**
   * Groups commands by command name and sorts subcommands for display.
   */
  private buildCommandGroups(commands: GlobalCommandDto[]): CommandGroupVm[] {
    const byCommand = new Map<string, GlobalCommandDto[]>();

    for (const command of commands) {
      const key = command.commandName;
      if (!byCommand.has(key)) {
        byCommand.set(key, []);
      }
      byCommand.get(key)!.push(command);
    }

    return Array.from(byCommand.entries())
      .map(([commandName, items]) => ({
        commandName,
        items: [...items].sort((a, b) => {
          const aIsRoot = !a.subCommandName;
          const bIsRoot = !b.subCommandName;

          if (aIsRoot && !bIsRoot) {
            return -1;
          }

          if (!aIsRoot && bIsRoot) {
            return 1;
          }

          return (a.subCommandName ?? '').localeCompare(b.subCommandName ?? '');
        })
      }))
      .sort((a, b) => a.commandName.localeCompare(b.commandName));
  }
}
