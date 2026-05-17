import { Component, Input, Output, EventEmitter, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { I18nService } from '../../services/i18n.service';
import { BoardConfigListItemDto, UpdateBoardConfigRequest } from '../../services/bot.service';

@Component({
  selector: 'app-board-config-edit-modal',
  imports: [CommonModule, FormsModule],
  templateUrl: './board-config-edit-modal.component.html',
  styleUrl: './board-config-edit-modal.component.css'
})
export class BoardConfigEditModalComponent implements OnChanges {
  @Input() isOpen = false;
  @Input() board: BoardConfigListItemDto | null = null;
  @Output() close = new EventEmitter<void>();
  @Output() save = new EventEmitter<UpdateBoardConfigRequest>();

  draft: UpdateBoardConfigRequest = {};

  constructor(public i18n: I18nService) {}

  ngOnChanges(): void {
    if (this.board) {
      this.draft = {
        boardType: this.board.boardType,
        boardChannelId: this.board.boardChannelId ?? '',
        boardMessageId: this.board.boardMessageId ?? '',
        boardTitle: this.board.boardTitle ?? '',
        boardDescriptionTemplate: this.board.boardDescriptionTemplate ?? '',
        subtitleLabel: this.board.subtitleLabel ?? '',
        contactLabel: this.board.contactLabel ?? ''
      };
    } else {
      this.draft = {};
    }
  }

  onSave(): void {
    const request: UpdateBoardConfigRequest = {
      boardType: this.draft.boardType,
      boardChannelId: this.draft.boardChannelId ? String(this.draft.boardChannelId) : undefined,
      boardMessageId: this.draft.boardMessageId ? String(this.draft.boardMessageId) : undefined,
      boardTitle: this.draft.boardTitle,
      boardDescriptionTemplate: this.draft.boardDescriptionTemplate,
      subtitleLabel: this.draft.subtitleLabel,
      contactLabel: this.draft.contactLabel
    };
    this.save.emit(request);
  }

  onClose(): void {
    this.close.emit();
  }
}
