import { Component, OnInit } from '@angular/core';
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
  templateUrl: './admin-commands.component.html',
  styleUrl: './admin-commands.component.css'
})
export class AdminCommandsComponent implements OnInit {
  commands: GlobalCommandDto[] = [];
  commandGroups: CommandGroupVm[] = [];
  loading = true;
  saveMessage = '';
  showEditModal = false;
  editingCommand: GlobalCommandDto | null = null;

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
    this.loading = true;
    this.commandsService.getGlobalCommands().subscribe({
      next: (data) => {
        this.commands = data;
        this.commandGroups = this.buildCommandGroups(data);
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });
  }

  /**
   * Opens the command edit modal.
   */
  openEditModal(command: GlobalCommandDto): void {
    this.editingCommand = command;
    this.showEditModal = true;
    this.saveMessage = '';
  }

  /**
   * Closes the command edit modal.
   */
  closeEditModal(): void {
    this.showEditModal = false;
    this.editingCommand = null;
  }

  /**
   * Saves edited command settings.
   */
  saveCommand(draft: UpdateGlobalCommandDto): void {
    if (!this.editingCommand) {
      return;
    }

    this.commandsService.updateGlobalCommand(this.editingCommand.commandName, this.editingCommand.subCommandName, draft).subscribe({
      next: (result) => {
        this.saveMessage = `${this.i18n.t('commands.saved')}: ${result.updated}`;
        this.toastr.success(this.saveMessage, this.i18n.t('commands.save'));
        this.loadCommands();
      },
      error: () => {
        this.saveMessage = this.i18n.t('commands.save_error');
        this.toastr.error(this.saveMessage, this.i18n.t('commands.save'));
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
