import { Component, Input, Output, EventEmitter, OnInit, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TeamDto, BotTeamsDto, BotService } from '../../services/bot.service';

type EditableTeamDto = TeamDto & {
  _deleted?: boolean;
  _isNew?: boolean;
};

@Component({
  selector: 'app-teams-edit-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './teams-edit-modal.component.html',
  styleUrl: './teams-edit-modal.component.css'
})
export class TeamsEditModalComponent implements OnInit, OnChanges {
  @Input() isOpen = false;
  @Input() teamsData: BotTeamsDto | null = null;
  @Input() isSaving = false;
  @Input() savePhase: 'idle' | 'saving' | 'syncing' = 'idle';
  @Output() close = new EventEmitter<void>();
  @Output() save = new EventEmitter<BotTeamsDto>();
  @Output() openJsonEditor = new EventEmitter<{ type: 'teams' | 'emojis', data: any }>();

  localTeams: EditableTeamDto[] = [];
  private originalTeamsSignature = '';
  editingTeamIndex: number | null = null;
  editingTeam: EditableTeamDto = { name: '', leaderName: '', contact: '', emoji: '' };
  newTeam: TeamDto = { name: '', leaderName: '', contact: '', emoji: '🎯' };
  showNewTeamForm = false;
  selectedEmojiTeamIndex: number | null = null;
  showEmojiPicker = false;
  closeArmed = false;
  footerNotice = '';
  shakeSaveButton = false;
  emojiCatalogLoaded = false;

  // Common emoji suggestions
  emojiSuggestions = [
    '⚔️', '🏹', '🛡️', '🔥', '❄️', '🦅', '🌿', '⚡',
    '💀', '🌑', '🌊', '🐺', '🎯', '🎖️', '⭐', '💥',
    '🚀', '🏆', '⚙️', '🔱', '🗡️', '🎪', '🎭', '🎸'
  ];

  constructor(private botService: BotService) {}

  /**
   * Initializes local editable teams copy.
   */
  ngOnInit(): void {
    this.hydrateFromInput();
    this.loadEmojiCatalog();
  }

  /**
   * Refreshes local editable teams copy when inputs change.
   */
  ngOnChanges(changes: SimpleChanges): void {
    if (changes['teamsData']) {
      this.hydrateFromInput();
    }

    if (!this.emojiCatalogLoaded) {
      this.loadEmojiCatalog();
    }
  }

  /**
   * Closes the modal and resets editor state.
   */
  closeModal(): void {
    if (this.isSaving) {
      this.footerNotice = 'Saving is in progress. Please wait.';
      this.triggerSaveAttention();
      return;
    }

    if (this.hasUnsavedWork() && !this.closeArmed) {
      this.footerNotice = 'Changes need to be saved. Press Cancel again to leave.';
      this.triggerSaveAttention();
      this.closeArmed = true;
      return;
    }

    this.close.emit();
    this.resetForm();
  }

  /**
   * Starts editing an existing team row.
   */
  startEditTeam(index: number): void {
    if (this.localTeams[index]?._deleted) {
      return;
    }

    this.editingTeamIndex = index;
    this.editingTeam = JSON.parse(JSON.stringify(this.localTeams[index]));
    this.footerNotice = '';
    this.closeArmed = false;
  }

  /**
   * Cancels team row editing.
   */
  cancelEdit(): void {
    this.editingTeamIndex = null;
    this.editingTeam = { name: '', leaderName: '', contact: '', emoji: '' };
  }

  /**
   * Applies current in-form edits to the selected team row.
   */
  saveTeamEdit(): void {
    if (this.editingTeamIndex !== null) {
      const emoji = this.normalizeEmoji(this.editingTeam.emoji);
      if (this.isEmojiUsedByAnotherTeam(emoji, this.editingTeamIndex)) {
        this.footerNotice = 'This emoji is already used by another team on this board.';
        this.triggerSaveAttention();
        return;
      }

      this.editingTeam.emoji = emoji;
      this.localTeams[this.editingTeamIndex] = this.editingTeam;
      this.editingTeamIndex = null;
      this.editingTeam = { name: '', leaderName: '', contact: '', emoji: '' };
      this.footerNotice = '';
      this.closeArmed = false;
    }
  }

  /**
   * Deletes a team row after user confirmation.
   */
  deleteTeam(index: number): void {
    const teamName = this.localTeams[index].name;
    if (confirm(`Delete team "${teamName}"?`)) {
      this.localTeams[index]._deleted = true;
      this.footerNotice = '';
      this.closeArmed = false;

      if (this.editingTeamIndex === index) {
        this.cancelEdit();
      }
    }
  }

  /**
   * Restores a team row previously marked for deletion.
   */
  undoDeleteTeam(index: number): void {
    if (!this.localTeams[index]) {
      return;
    }

    this.localTeams[index]._deleted = false;
    this.footerNotice = '';
    this.closeArmed = false;
  }

  /**
   * Opens the new-team form.
   */
  startNewTeam(): void {
    this.showNewTeamForm = true;
    this.newTeam = { name: '', leaderName: '', contact: '', emoji: '🎯' };
    this.footerNotice = '';
    this.closeArmed = false;
  }

  /**
   * Cancels new-team creation.
   */
  cancelNewTeam(): void {
    this.showNewTeamForm = false;
    this.newTeam = { name: '', leaderName: '', contact: '', emoji: '🎯' };
  }

  /**
   * Adds a new team if required fields are present.
   */
  addTeam(): void {
    if (this.newTeam.name && this.newTeam.leaderName) {
      const emoji = this.normalizeEmoji(this.newTeam.emoji);
      if (this.isEmojiUsedByAnotherTeam(emoji, -1)) {
        this.footerNotice = 'This emoji is already used by another team on this board.';
        this.triggerSaveAttention();
        return;
      }

      const addedTeam: EditableTeamDto = {
        ...JSON.parse(JSON.stringify(this.newTeam)),
        emoji,
        _isNew: true,
        _deleted: false
      };
      this.localTeams.push(addedTeam);
      this.cancelNewTeam();
      this.footerNotice = '';
      this.closeArmed = false;
    }
  }

  /**
   * Emits all edited teams and closes the modal.
   */
  saveChanges(): void {
    if (this.isSaving) {
      this.footerNotice = 'Saving is already in progress.';
      this.triggerSaveAttention();
      return;
    }

    if (this.hasPendingNewTeamDraft()) {
      this.footerNotice = 'You have a new team draft. Add it first or cancel it before saving.';
      this.triggerSaveAttention();
      return;
    }

    if (!this.hasRealChanges()) {
      this.footerNotice = 'No changes to save.';
      this.triggerSaveAttention();
      return;
    }

    if (this.editingTeamIndex !== null) {
      this.footerNotice = 'Finish team editing first by pressing Save in the row editor.';
      this.triggerSaveAttention();
      return;
    }

    const dto: BotTeamsDto = {
      teams: this.localTeams
        .filter(team => !team._deleted)
        .map(team => ({
          teamId: team.teamId,
          name: team.name,
          leaderName: team.leaderName,
          contact: team.contact,
          emoji: team.emoji
        }))
    };

    this.footerNotice = '';
    this.closeArmed = false;
    this.save.emit(dto);
  }

  /**
   * Opens manual JSON editing for the teams array.
   */
  editJsonManually(): void {
    this.openJsonEditor.emit({ type: 'teams', data: this.localTeams });
  }

  /**
   * Opens emoji picker for selected team index.
   */
  openEmojiPicker(teamIndex: number): void {
    if (teamIndex >= 0 && this.localTeams[teamIndex]?._deleted) {
      return;
    }

    this.selectedEmojiTeamIndex = teamIndex;
    this.showEmojiPicker = true;
  }

  /**
   * Applies selected emoji to active target and closes picker.
   */
  selectEmoji(emoji: string): void {
    const targetIndex = this.editingTeamIndex !== null
      ? this.editingTeamIndex
      : (this.selectedEmojiTeamIndex !== null && this.selectedEmojiTeamIndex >= 0 ? this.selectedEmojiTeamIndex : null);
    if (this.isEmojiUsedByAnotherTeam(emoji, targetIndex ?? null)) {
      this.footerNotice = 'This emoji is already used by another team on this board.';
      this.triggerSaveAttention();
      return;
    }

    if (this.editingTeamIndex !== null) {
      this.editingTeam.emoji = emoji;
    } else if (this.selectedEmojiTeamIndex !== null && this.selectedEmojiTeamIndex >= 0) {
      this.localTeams[this.selectedEmojiTeamIndex].emoji = emoji;
    } else {
      this.newTeam.emoji = emoji;
    }
    this.showEmojiPicker = false;
    this.selectedEmojiTeamIndex = null;
  }

  /**
   * Returns true when emoji is already assigned to another team on this board.
   */
  isEmojiTakenInPicker(emoji: string): boolean {
    return this.getEmojiTakenByTeamName(emoji) !== null;
  }

  /**
   * Returns the team name currently using the emoji in this picker context.
   */
  getEmojiTakenByTeamName(emoji: string): string | null {
    const targetIndex = this.editingTeamIndex !== null
      ? this.editingTeamIndex
      : (this.selectedEmojiTeamIndex !== null && this.selectedEmojiTeamIndex >= 0 ? this.selectedEmojiTeamIndex : null);
    const normalized = this.normalizeEmoji(emoji);

    const owner = this.localTeams.find((team, index) => {
      if (team._deleted) {
        return false;
      }

      if (targetIndex !== null && index === targetIndex) {
        return false;
      }

      return this.normalizeEmoji(team.emoji) === normalized;
    });

    return owner?.name ?? null;
  }

  /**
   * Closes emoji picker without changes.
   */
  closeEmojiPicker(): void {
    this.showEmojiPicker = false;
    this.selectedEmojiTeamIndex = null;
  }

  /**
   * Resets all modal editing state to defaults.
   */
  resetForm(): void {
    this.editingTeamIndex = null;
    this.editingTeam = { name: '', leaderName: '', contact: '', emoji: '' };
    this.showNewTeamForm = false;
    this.newTeam = { name: '', leaderName: '', contact: '', emoji: '🎯' };
    this.showEmojiPicker = false;
    this.selectedEmojiTeamIndex = null;
    this.footerNotice = '';
    this.closeArmed = false;
    this.shakeSaveButton = false;
  }

  /**
   * Indicates whether Save All Changes button should be enabled.
   */
  canSaveChanges(): boolean {
    if (this.isSaving) {
      return false;
    }

    return this.hasRealChanges() || this.hasPendingNewTeamDraft() || this.editingTeamIndex !== null;
  }

  /**
   * Returns true if there are persisted changes waiting to be saved.
   */
  hasRealChanges(): boolean {
    const currentSignature = this.serializeTeams(this.localTeams.filter(t => !t._deleted));
    return currentSignature !== this.originalTeamsSignature;
  }

  /**
   * Returns true if the user started adding a team but did not submit it yet.
   */
  hasPendingNewTeamDraft(): boolean {
    if (!this.showNewTeamForm) {
      return false;
    }

    return !!(this.newTeam.name?.trim() || this.newTeam.leaderName?.trim() || this.newTeam.contact?.trim());
  }

  /**
   * Returns whether modal contains any unsaved work.
   */
  hasUnsavedWork(): boolean {
    return this.hasRealChanges() || this.hasPendingNewTeamDraft() || this.editingTeamIndex !== null;
  }

  private hydrateFromInput(): void {
    if (!this.teamsData) {
      this.localTeams = [];
      this.originalTeamsSignature = '';
      return;
    }

    const clonedTeams: EditableTeamDto[] = JSON.parse(JSON.stringify(this.teamsData.teams));
    this.localTeams = clonedTeams.map(team => ({ ...team, _deleted: false, _isNew: false }));
    this.originalTeamsSignature = this.serializeTeams(this.localTeams);
    this.footerNotice = '';
    this.closeArmed = false;
    this.shakeSaveButton = false;
  }

  private serializeTeams(teams: EditableTeamDto[]): string {
    const normalized = teams.map(team => ({
      teamId: team.teamId ?? null,
      name: team.name?.trim() ?? '',
      leaderName: team.leaderName?.trim() ?? '',
      contact: team.contact?.trim() ?? '',
      emoji: team.emoji?.trim() ?? ''
    }));

    return JSON.stringify(normalized);
  }

  private triggerSaveAttention(): void {
    this.shakeSaveButton = false;
    setTimeout(() => {
      this.shakeSaveButton = true;
      setTimeout(() => (this.shakeSaveButton = false), 500);
    }, 0);
  }

  private loadEmojiCatalog(): void {
    this.botService.getEmojiCatalog().subscribe({
      next: (catalog) => {
        if (Array.isArray(catalog) && catalog.length > 0) {
          this.emojiSuggestions = Array.from(new Set(catalog.filter(e => typeof e === 'string' && e.trim().length > 0))).slice(0, 500);
        }
        this.emojiCatalogLoaded = true;
      },
      error: () => {
        this.emojiCatalogLoaded = true;
      }
    });
  }

  private isEmojiUsedByAnotherTeam(emoji: string, targetIndex: number | null): boolean {
    const normalized = this.normalizeEmoji(emoji);
    return this.localTeams.some((team, index) => {
      if (team._deleted) {
        return false;
      }

      if (targetIndex !== null && index === targetIndex) {
        return false;
      }

      return this.normalizeEmoji(team.emoji) === normalized;
    });
  }

  private normalizeEmoji(emoji: string | undefined): string {
    return !emoji || !emoji.trim() ? '🎯' : emoji.trim();
  }
}

