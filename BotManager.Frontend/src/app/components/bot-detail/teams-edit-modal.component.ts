import { Component, Input, Output, EventEmitter, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TeamDto, BotTeamsDto } from '../../services/bot.service';

@Component({
  selector: 'app-teams-edit-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './teams-edit-modal.component.html',
  styleUrl: './teams-edit-modal.component.css'
})
export class TeamsEditModalComponent implements OnInit {
  @Input() isOpen = false;
  @Input() teamsData: BotTeamsDto | null = null;
  @Output() close = new EventEmitter<void>();
  @Output() save = new EventEmitter<BotTeamsDto>();
  @Output() openJsonEditor = new EventEmitter<{ type: 'teams' | 'emojis', data: any }>();

  localTeams: TeamDto[] = [];
  editingTeamIndex: number | null = null;
  editingTeam: TeamDto = { name: '', leaderName: '', contact: '', emoji: '' };
  newTeam: TeamDto = { name: '', leaderName: '', contact: '', emoji: '🎯' };
  showNewTeamForm = false;
  selectedEmojiTeamIndex: number | null = null;
  showEmojiPicker = false;

  // Common emoji suggestions
  emojiSuggestions = [
    '⚔️', '🏹', '🛡️', '🔥', '❄️', '🦅', '🌿', '⚡',
    '💀', '🌑', '🌊', '🐺', '🎯', '🎖️', '⭐', '💥',
    '🚀', '🏆', '⚙️', '🔱', '🗡️', '🎪', '🎭', '🎸'
  ];

  ngOnInit(): void {
    if (this.teamsData) {
      this.localTeams = JSON.parse(JSON.stringify(this.teamsData.teams));
    }
  }

  ngOnChanges(): void {
    if (this.teamsData) {
      this.localTeams = JSON.parse(JSON.stringify(this.teamsData.teams));
    }
  }

  closeModal(): void {
    this.close.emit();
    this.resetForm();
  }

  startEditTeam(index: number): void {
    this.editingTeamIndex = index;
    this.editingTeam = JSON.parse(JSON.stringify(this.localTeams[index]));
  }

  cancelEdit(): void {
    this.editingTeamIndex = null;
    this.editingTeam = { name: '', leaderName: '', contact: '', emoji: '' };
  }

  saveTeamEdit(): void {
    if (this.editingTeamIndex !== null) {
      this.localTeams[this.editingTeamIndex] = this.editingTeam;
      this.editingTeamIndex = null;
      this.editingTeam = { name: '', leaderName: '', contact: '', emoji: '' };
    }
  }

  deleteTeam(index: number): void {
    const teamName = this.localTeams[index].name;
    if (confirm(`Delete team "${teamName}"?`)) {
      this.localTeams.splice(index, 1);
    }
  }

  startNewTeam(): void {
    this.showNewTeamForm = true;
    this.newTeam = { name: '', leaderName: '', contact: '', emoji: '🎯' };
  }

  cancelNewTeam(): void {
    this.showNewTeamForm = false;
    this.newTeam = { name: '', leaderName: '', contact: '', emoji: '🎯' };
  }

  addTeam(): void {
    if (this.newTeam.name && this.newTeam.leaderName) {
      this.localTeams.push(JSON.parse(JSON.stringify(this.newTeam)));
      this.cancelNewTeam();
    }
  }

  saveChanges(): void {
    const dto: BotTeamsDto = {
      teams: this.localTeams
    };
    this.save.emit(dto);
    this.closeModal();
  }

  editJsonManually(): void {
    this.openJsonEditor.emit({ type: 'teams', data: this.localTeams });
  }

  openEmojiPicker(teamIndex: number): void {
    this.selectedEmojiTeamIndex = teamIndex;
    this.showEmojiPicker = true;
  }

  selectEmoji(emoji: string): void {
    if (this.editingTeamIndex !== null) {
      this.editingTeam.emoji = emoji;
    } else if (this.selectedEmojiTeamIndex !== null) {
      this.localTeams[this.selectedEmojiTeamIndex].emoji = emoji;
    } else {
      this.newTeam.emoji = emoji;
    }
    this.showEmojiPicker = false;
    this.selectedEmojiTeamIndex = null;
  }

  closeEmojiPicker(): void {
    this.showEmojiPicker = false;
    this.selectedEmojiTeamIndex = null;
  }

  resetForm(): void {
    this.editingTeamIndex = null;
    this.editingTeam = { name: '', leaderName: '', contact: '', emoji: '' };
    this.showNewTeamForm = false;
    this.newTeam = { name: '', leaderName: '', contact: '', emoji: '🎯' };
    this.showEmojiPicker = false;
  }
}

