import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { BotConfigurationDto } from '../../services/bot.service';

@Component({
  selector: 'app-config-edit-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './config-edit-modal.component.html',
  styleUrl: './config-edit-modal.component.css'
})
export class ConfigEditModalComponent {
  @Input() isOpen = false;
  @Input() config: BotConfigurationDto | null | undefined = null;
  @Output() close = new EventEmitter<void>();
  @Output() save = new EventEmitter<BotConfigurationDto>();

  localConfig: BotConfigurationDto = {};
  loading = false;

  /**
   * Copies input config into local editable state when inputs change.
   */
  ngOnChanges(): void {
    if (this.config) {
      this.localConfig = { ...this.config };
    }
  }

  /**
   * Closes the modal.
   */
  closeModal(): void {
    this.close.emit();
  }

  /**
   * Emits save event with local config and closes modal.
   */
  saveConfig(): void {
    this.loading = true;
    this.save.emit(this.localConfig);
    setTimeout(() => {
      this.loading = false;
      this.closeModal();
    }, 300);
  }
}
