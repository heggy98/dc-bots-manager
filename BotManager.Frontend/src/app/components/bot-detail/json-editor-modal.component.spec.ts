import { ComponentFixture, TestBed } from '@angular/core/testing';

import { JsonEditorModalComponent } from './json-editor-modal.component';

describe('JsonEditorModalComponent', () => {
  let fixture: ComponentFixture<JsonEditorModalComponent>;
  let component: JsonEditorModalComponent;
  const data = { teams: [{ name: 'A' }] };
  const expected = JSON.stringify(data, null, 2);

  const textarea = () => fixture.nativeElement.querySelector('textarea.json-editor') as HTMLTextAreaElement | null;

  const open = async () => {
    fixture.componentRef.setInput('isOpen', true);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  const close = () => {
    component.closeModal();
    fixture.componentRef.setInput('isOpen', false);
    fixture.detectChanges();
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [JsonEditorModalComponent] }).compileComponents();
    fixture = TestBed.createComponent(JsonEditorModalComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('data', data);
    fixture.detectChanges();
  });

  it('shows the current data when opened', async () => {
    await open();
    expect(component.jsonText).toBe(expected);
    expect(textarea()?.value).toBe(expected);
    expect(component.isJsonValid).toBeTrue();
  });

  it('shows the same data again after close and reopen', async () => {
    await open();
    close();

    await open();
    expect(component.jsonText).toBe(expected);
    expect(textarea()?.value).toBe(expected);
    expect(component.isJsonValid).toBeTrue();
  });

  it('discards unsaved edits when reopened', async () => {
    await open();
    component.jsonText = '{ broken';
    close();

    await open();
    expect(component.jsonText).toBe(expected);
  });

  it('picks up new data passed while open', async () => {
    await open();
    const next = { emojis: ['x'] };
    fixture.componentRef.setInput('data', next);
    fixture.detectChanges();
    expect(component.jsonText).toBe(JSON.stringify(next, null, 2));
  });

  it('emits parsed JSON on save and closes', async () => {
    await open();
    let saved: any;
    let closed = false;
    component.save.subscribe(v => (saved = v));
    component.close.subscribe(() => (closed = true));
    component.saveJson();
    expect(saved).toEqual(data);
    expect(closed).toBeTrue();
  });
});
