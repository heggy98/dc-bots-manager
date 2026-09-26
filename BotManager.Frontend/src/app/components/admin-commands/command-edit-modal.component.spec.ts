import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';

import { CommandEditModalComponent } from './command-edit-modal.component';
import { GlobalCommandDto, UpdateGlobalCommandDto } from '../../services/commands.service';

describe('CommandEditModalComponent (OnPush)', () => {
  let fixture: ComponentFixture<CommandEditModalComponent>;
  let component: CommandEditModalComponent;

  const command: GlobalCommandDto = {
    commandName: 'board',
    subCommandName: 'show',
    description: 'Shows the board',
    minimumPermissionLevel: 2,
    isEnabled: true,
    botCount: 1,
    hasDifferencesAcrossBots: false
  };

  const el = () => fixture.nativeElement as HTMLElement;
  const input = (name: string) => el().querySelector(`input[name="${name}"]`) as HTMLInputElement;

  const render = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CommandEditModalComponent] }).compileComponents();
    fixture = TestBed.createComponent(CommandEditModalComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('command', command);
    fixture.componentRef.setInput('isOpen', true);
    await render();
  });

  it('fills the form from the command', () => {
    expect(el().querySelector('.modal-header h3')?.textContent).toContain('/board');
    expect(input('description').value).toBe('Shows the board');
    expect(input('isEnabled').checked).toBeTrue();
  });

  it('updates the draft immutably from form input without touching the command', () => {
    const before = component.draft();
    const field = input('description');
    field.value = 'New text';
    field.dispatchEvent(new Event('input'));
    input('isEnabled').click();

    expect(component.draft()).not.toBe(before);
    expect(component.draft()?.description).toBe('New text');
    expect(component.draft()?.isEnabled).toBeFalse();
    expect(command.description).toBe('Shows the board');
  });

  it('emits the draft on save, shows saving state and closes after the delay', fakeAsync(() => {
    let saved: UpdateGlobalCommandDto | undefined;
    let closed = false;
    component.save.subscribe(d => (saved = d));
    component.close.subscribe(() => (closed = true));

    component.saveCommand();
    fixture.detectChanges();
    const submit = el().querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(submit.disabled).toBeTrue();
    expect(submit.textContent).toContain('Saving...');
    expect(saved?.minimumPermissionLevel).toBe(2);

    tick(300);
    fixture.detectChanges();
    expect(closed).toBeTrue();
    expect(component.loading()).toBeFalse();
    expect(el().querySelector('form')).toBeNull();
  }));

  it('re-initializes the draft when reopened with the same command', async () => {
    component.closeModal();
    fixture.componentRef.setInput('isOpen', false);
    fixture.detectChanges();

    fixture.componentRef.setInput('isOpen', true);
    await render();
    expect(input('description').value).toBe('Shows the board');
  });
});
