import { ChangeDetectionStrategy, ChangeDetectorRef, Component, Input, Output, EventEmitter, inject } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { BotConfigurationDto } from '../../services/bot.service';

@Component({
  selector: 'app-config-edit-modal',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './config-edit-modal.component.html',
  styleUrl: './config-edit-modal.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ConfigEditModalComponent {
  @Input() isOpen = false;
  @Input() config: BotConfigurationDto | null | undefined = null;
  @Output() close = new EventEmitter<void>();
  @Output() save = new EventEmitter<BotConfigurationDto>();

  localConfig: BotConfigurationDto = {};
  loading = false;
  private readonly cdr = inject(ChangeDetectorRef);

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
      this.cdr.markForCheck();
      this.closeModal();
    }, 300);
  }
}
