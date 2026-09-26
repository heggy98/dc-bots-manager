import { ChangeDetectionStrategy, Component, DestroyRef, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { GlobalCommandDto, UpdateGlobalCommandDto } from '../../services/commands.service';

@Component({
  selector: 'app-command-edit-modal',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './command-edit-modal.component.html',
  styleUrl: './command-edit-modal.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CommandEditModalComponent implements OnChanges {
  @Input() isOpen = false;
  @Input() command: GlobalCommandDto | null = null;
  @Output() close = new EventEmitter<void>();
  @Output() save = new EventEmitter<UpdateGlobalCommandDto>();

  readonly draft = signal<UpdateGlobalCommandDto | null>(null);
  readonly loading = signal(false);

  private saveTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      if (this.saveTimer) clearTimeout(this.saveTimer);
    });
  }

  /**
   * Synchronizes local draft data when the selected command changes or the modal is (re)opened.
   */
  ngOnChanges(changes: SimpleChanges): void {
    const opened = !!changes['isOpen'] && this.isOpen;
    if ((opened || changes['command']) && this.command) {
      const command = this.command;
      this.draft.set({
        description: command.description,
        minimumPermissionLevel: command.minimumPermissionLevel,
        isEnabled: command.isEnabled,
        userHint: command.userHint,
        successMessage: command.successMessage,
        permissionMessage: command.permissionMessage,
        errorMessage: command.errorMessage,
        adminOnlyMessage: command.adminOnlyMessage,
        invalidArgumentsMessage: command.invalidArgumentsMessage
      });
    }
  }

  /**
   * Immutably updates a single draft field from a form control.
   */
  updateDraft<K extends keyof UpdateGlobalCommandDto>(key: K, value: UpdateGlobalCommandDto[K]): void {
    this.draft.update(d => (d ? { ...d, [key]: value } : d));
  }

  /**
   * Closes the modal and resets local state.
   */
  closeModal(): void {
    this.close.emit();
    this.draft.set(null);
  }

  /**
   * Emits save event with current draft values.
   */
  saveCommand(): void {
    const draft = this.draft();
    if (!draft) {
      return;
    }

    this.loading.set(true);
    this.save.emit(draft);
    this.saveTimer = setTimeout(() => {
      this.saveTimer = null;
      this.loading.set(false);
      this.closeModal();
    }, 300);
  }
}
