import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { I18nService } from '../../services/i18n.service';
import { BotConfigImportSummary, BotConfigTransferService, MAX_IMPORT_BYTES } from '../../services/bot-config-transfer.service';

/**
 * Export (download .json) and import (file -> server-side dry run preview -> confirm) of a bot's
 * configuration. The export never contains the bot token.
 */
@Component({
  selector: 'app-bot-config-transfer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './bot-config-transfer.component.html',
  styleUrl: './bot-config-transfer.component.css'
})
export class BotConfigTransferComponent {
  readonly i18n = inject(I18nService);
  private readonly transferService = inject(BotConfigTransferService);
  private readonly toastr = inject(ToastrService);

  readonly botId = input.required<number>();
  /** Emitted after a successful import so the parent can reload its data. */
  readonly imported = output<BotConfigImportSummary>();

  readonly exporting = signal(false);
  readonly busy = signal(false);
  readonly fileName = signal('');
  readonly preview = signal<BotConfigImportSummary | null>(null);
  readonly errors = signal<string[]>([]);
  readonly applyDiscordIds = signal(true);
  private document: unknown = null;

  /** Downloads the configuration as a pretty-printed JSON file. */
  exportConfig(): void {
    this.exporting.set(true);
    this.transferService.exportConfig(this.botId()).subscribe({
      next: doc => {
        this.exporting.set(false);
        const blob = new Blob([JSON.stringify(doc, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `bot-${this.botId()}-config-${new Date().toISOString().slice(0, 10)}.json`;
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 0);
        this.toastr.success(this.i18n.t('transfer.export_success'), this.i18n.t('transfer.title'));
      },
      error: () => {
        this.exporting.set(false);
        this.toastr.error(this.i18n.t('transfer.export_error'), this.i18n.t('transfer.title'));
      }
    });
  }

  /** Reads the chosen file and asks the server for a dry-run summary. */
  async onFileSelected(event: Event): Promise<void> {
    const inputEl = event.target as HTMLInputElement;
    const file = inputEl.files?.[0];
    inputEl.value = '';
    if (!file) return;
    this.reset();
    this.fileName.set(file.name);

    if (file.size > MAX_IMPORT_BYTES) {
      this.errors.set([this.i18n.t('transfer.too_large')]);
      return;
    }

    let parsed: unknown;
    try {
      parsed = JSON.parse(await file.text());
    } catch {
      this.errors.set([this.i18n.t('transfer.invalid_json')]);
      return;
    }

    this.document = parsed;
    this.runImport(true);
  }

  /** Re-runs the dry run when the Discord-id option changes, so the preview matches what will be applied. */
  setApplyDiscordIds(value: boolean): void {
    this.applyDiscordIds.set(value);
    if (this.document) this.runImport(true);
  }

  confirmImport(): void {
    if (!this.document || !this.preview()) return;
    this.runImport(false);
  }

  cancel(): void {
    this.reset();
  }

  private runImport(dryRun: boolean): void {
    this.busy.set(true);
    this.errors.set([]);
    this.transferService.importConfig(this.botId(), this.document, { dryRun, applyDiscordIds: this.applyDiscordIds() }).subscribe({
      next: summary => {
        this.busy.set(false);
        if (dryRun) {
          this.preview.set(summary);
          return;
        }
        this.toastr.success(this.i18n.t('transfer.import_success'), this.i18n.t('transfer.title'));
        this.reset();
        this.imported.emit(summary);
      },
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);
        this.preview.set(null);
        const serverErrors = Array.isArray(err?.error?.errors) ? err.error.errors as string[] : [];
        this.errors.set(serverErrors.length > 0
          ? serverErrors
          : [err?.status === 413 ? this.i18n.t('transfer.too_large') : this.i18n.t('transfer.import_error')]);
      }
    });
  }

  private reset(): void {
    this.document = null;
    this.preview.set(null);
    this.errors.set([]);
    this.fileName.set('');
  }
}
