import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideToastr } from 'ngx-toastr';

import { BotDetailComponent } from './bot-detail.component';

describe('BotDetailComponent', () => {
  let component: BotDetailComponent;
  let fixture: ComponentFixture<BotDetailComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BotDetailComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideToastr()]
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
