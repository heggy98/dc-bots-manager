import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BotDetailComponent } from './bot-detail.component';

describe('BotDetailComponent', () => {
  let component: BotDetailComponent;
  let fixture: ComponentFixture<BotDetailComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BotDetailComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(BotDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
