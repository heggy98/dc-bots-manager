import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-json-editor-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './json-editor-modal.component.html',
  styleUrl: './json-editor-modal.component.css'
})
export class JsonEditorModalComponent {
  @Input() isOpen = false;
  @Input() type: 'teams' | 'emojis' = 'teams';
  @Input() data: any = null;
  @Output() close = new EventEmitter<void>();
  @Output() save = new EventEmitter<any>();

  jsonText = '';
  error = '';
  hint = '';
  isJsonValid = false;

  ngOnChanges(): void {
    if (this.data) {
      try {
        this.jsonText = JSON.stringify(this.data, null, 2);
        this.validateJson();
      } catch (e) {
        this.error = 'Error parsing JSON';
        this.hint = 'Unable to prepare JSON editor content.';
        this.isJsonValid = false;
      }
    } else {
      this.jsonText = '';
      this.validateJson();
    }
  }

  closeModal(): void {
    this.close.emit();
    this.jsonText = '';
    this.error = '';
    this.hint = '';
    this.isJsonValid = false;
  }

  onJsonInput(): void {
    this.validateJson();
  }

  private validateJson(): void {
    if (!this.jsonText || !this.jsonText.trim()) {
      this.error = 'JSON is empty.';
      this.hint = 'Enter valid JSON (object or array) before saving.';
      this.isJsonValid = false;
      return;
    }

    try {
      JSON.parse(this.jsonText);
      this.error = '';
      this.hint = 'JSON is valid and ready to save.';
      this.isJsonValid = true;
    } catch (e: any) {
      this.error = 'Invalid JSON';
      this.hint = this.buildParseHint(e?.message || 'Unknown JSON parse error.');
      this.isJsonValid = false;
    }
  }

  private buildParseHint(message: string): string {
    const posMatch = message.match(/position\s+(\d+)/i);
    if (!posMatch) {
      return message;
    }

    const position = Number(posMatch[1]);
    if (Number.isNaN(position) || position < 0) {
      return message;
    }

    const before = this.jsonText.slice(0, position);
    const lines = before.split('\n');
    const line = lines.length;
    const column = lines[lines.length - 1].length + 1;
    return `${message} (line ${line}, column ${column})`;
  }

  saveJson(): void {
    this.validateJson();
    if (!this.isJsonValid) {
      return;
    }

    const parsed = JSON.parse(this.jsonText);
    this.save.emit(parsed);
    this.closeModal();
  }
}
