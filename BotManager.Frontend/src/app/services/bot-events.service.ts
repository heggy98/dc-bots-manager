import { Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { AuthService } from './auth.service';

export interface BotStatusChangedEvent {
  botId: number;
  status: number;
  statusText: string;
  timestamp: string;
}

export interface NewBotLogEvent {
  botId: number;
  timestamp: string;
  level: string;
  message: string;
}

export interface BotStatsUpdatedEvent {
  botId: number;
  requests24h: number;
  errors24h: number;
}

export interface BotHistoryUpdatedEvent {
  botId: number;
}

@Injectable({ providedIn: 'root' })
export class BotEventsService {
  private hubConnection: HubConnection | null = null;
  private subscribedBotId: number | null = null;

  readonly statusChanged$ = new Subject<BotStatusChangedEvent>();
  readonly newLog$ = new Subject<NewBotLogEvent>();
  readonly statsUpdated$ = new Subject<BotStatsUpdatedEvent>();
  readonly historyUpdated$ = new Subject<BotHistoryUpdatedEvent>();

  constructor(private authService: AuthService) {}

  async connectAndJoin(botId: number): Promise<void> {
    if (this.hubConnection && this.hubConnection.state === HubConnectionState.Connected) {
      if (this.subscribedBotId !== botId) {
        if (this.subscribedBotId !== null) {
          await this.hubConnection.invoke('LeaveBotGroup', this.subscribedBotId);
        }
        await this.hubConnection.invoke('JoinBotGroup', botId);
        this.subscribedBotId = botId;
      }
      return;
    }

    const tokenFactory = () => this.authService.getToken() ?? '';

    this.hubConnection = new HubConnectionBuilder()
      .withUrl('/hubs/bot-events', {
        accessTokenFactory: tokenFactory
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.registerHandlers(this.hubConnection);

    await this.hubConnection.start();
    await this.hubConnection.invoke('JoinBotGroup', botId);
    this.subscribedBotId = botId;
  }

  async disconnect(): Promise<void> {
    if (!this.hubConnection) {
      return;
    }

    try {
      if (this.subscribedBotId !== null && this.hubConnection.state === HubConnectionState.Connected) {
        await this.hubConnection.invoke('LeaveBotGroup', this.subscribedBotId);
      }
    } catch {
      // best effort on shutdown
    }

    await this.hubConnection.stop();
    this.hubConnection = null;
    this.subscribedBotId = null;
  }

  private registerHandlers(connection: HubConnection): void {
    connection.on('BotStatusChanged', (event: BotStatusChangedEvent) => {
      this.statusChanged$.next(event);
    });

    connection.on('NewBotLog', (event: NewBotLogEvent) => {
      this.newLog$.next(event);
    });

    connection.on('BotStatsUpdated', (event: BotStatsUpdatedEvent) => {
      this.statsUpdated$.next(event);
    });

    connection.on('BotHistoryUpdated', (event: BotHistoryUpdatedEvent) => {
      this.historyUpdated$.next(event);
    });
  }
}
