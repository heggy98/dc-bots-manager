import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { GlobalCommandDto, UpdateGlobalCommandDto } from '../../services/commands.service';

@Component({
  selector: 'app-command-edit-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './command-edit-modal.component.html',
  styleUrl: './command-edit-modal.component.css'
})
export class CommandEditModalComponent {
  @Input() isOpen = false;
  @Input() command: GlobalCommandDto | null = null;
  @Output() close = new EventEmitter<void>();
  @Output() save = new EventEmitter<UpdateGlobalCommandDto>();

  draft: UpdateGlobalCommandDto | null = null;
  loading = false;

  /**
   * Synchronizes local draft data when selected command changes.
   */
  ngOnChanges(): void {
    if (this.command) {
      this.draft = {
        description: this.command.description,
        minimumPermissionLevel: this.command.minimumPermissionLevel,
        isEnabled: this.command.isEnabled,
        userHint: this.command.userHint,
        successMessage: this.command.successMessage,
        permissionMessage: this.command.permissionMessage,
        errorMessage: this.command.errorMessage,
        adminOnlyMessage: this.command.adminOnlyMessage,
        invalidArgumentsMessage: this.command.invalidArgumentsMessage
      };
    }
  }

  /**
   * Closes the modal and resets local state.
   */
  closeModal(): void {
    this.close.emit();
    this.draft = null;
  }

  /**
   * Emits save event with current draft values.
   */
  saveCommand(): void {
    if (!this.draft) {
      return;
    }

    this.loading = true;
    this.save.emit(this.draft);
    setTimeout(() => {
      this.loading = false;
      this.closeModal();
    }, 300);
  }
}
