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
  /** Bot the most recent caller wants to be subscribed to (null after disconnect). */
  private desiredBotId: number | null = null;
  /** Serializes connect/disconnect/group operations so they never interleave. */
  private operationQueue: Promise<void> = Promise.resolve();

  readonly statusChanged$ = new Subject<BotStatusChangedEvent>();
  readonly newLog$ = new Subject<NewBotLogEvent>();
  readonly statsUpdated$ = new Subject<BotStatsUpdatedEvent>();
  readonly historyUpdated$ = new Subject<BotHistoryUpdatedEvent>();

  constructor(private authService: AuthService) {}

  /**
   * Returns true when the hub connection is currently established.
   */
  isConnected(): boolean {
    return this.hubConnection?.state === HubConnectionState.Connected;
  }

  /**
   * Connects to the hub (if needed) and subscribes to events for the given bot.
   */
  connectAndJoin(botId: number): Promise<void> {
    this.desiredBotId = botId;
    return this.enqueue(() => this.doConnectAndJoin(botId));
  }

  /**
   * Leaves the current bot group and stops the connection unless a newer subscription is pending.
   */
  disconnect(): Promise<void> {
    this.desiredBotId = null;
    return this.enqueue(() => this.doDisconnect());
  }

  private enqueue(operation: () => Promise<void>): Promise<void> {
    const run = this.operationQueue.then(operation, operation);
    this.operationQueue = run.catch(() => undefined);
    return run;
  }

  private async doConnectAndJoin(botId: number): Promise<void> {
    if (this.desiredBotId !== botId) {
      // Superseded by a newer connect/disconnect request.
      return;
    }

    if (!(await this.authService.ensureFreshSession())) {
      await this.stopConnection();
      return;
    }

    const existing = this.hubConnection;
    if (existing && existing.state === HubConnectionState.Connected) {
      await this.switchGroup(existing, botId);
      return;
    }

    if (existing && existing.state === HubConnectionState.Reconnecting) {
      // onreconnected will join the desired group once the connection is back.
      return;
    }

    await this.stopConnection();

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/bot-events', {
        // Auth uses the HttpOnly access cookie; the factory only refreshes it before (re)connects.
        accessTokenFactory: async () => {
          await this.authService.ensureFreshSession();
          return '';
        }
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.registerHandlers(connection);

    connection.onreconnected(() => {
      void this.enqueue(async () => {
        if (this.hubConnection !== connection) {
          return;
        }
        // Group membership is per connection id and is lost on reconnect.
        this.subscribedBotId = null;
        if (this.desiredBotId !== null) {
          await this.switchGroup(connection, this.desiredBotId);
        }
      });
    });

    connection.onclose(() => {
      if (this.hubConnection === connection) {
        this.hubConnection = null;
        this.subscribedBotId = null;
      }
    });

    this.hubConnection = connection;
    try {
      await connection.start();
    } catch (err) {
      if (this.hubConnection === connection) {
        this.hubConnection = null;
      }
      throw err;
    }

    await connection.invoke('JoinBotGroup', botId);
    this.subscribedBotId = botId;
  }

  private async doDisconnect(): Promise<void> {
    if (!this.hubConnection) {
      return;
    }

    if (this.desiredBotId !== null) {
      // A newer connectAndJoin is queued; keep the connection and let it switch groups.
      return;
    }

    await this.stopConnection();
  }

  private async switchGroup(connection: HubConnection, botId: number): Promise<void> {
    if (this.subscribedBotId === botId) {
      return;
    }

    if (this.subscribedBotId !== null) {
      try {
        await connection.invoke('LeaveBotGroup', this.subscribedBotId);
      } catch {
        // best effort
      }
      this.subscribedBotId = null;
    }

    await connection.invoke('JoinBotGroup', botId);
    this.subscribedBotId = botId;
  }

  private async stopConnection(): Promise<void> {
    const connection = this.hubConnection;
    if (!connection) {
      return;
    }

    try {
      if (this.subscribedBotId !== null && connection.state === HubConnectionState.Connected) {
        await connection.invoke('LeaveBotGroup', this.subscribedBotId);
      }
    } catch {
      // best effort on shutdown
    }

    this.hubConnection = null;
    this.subscribedBotId = null;

    try {
      await connection.stop();
    } catch {
      // best effort on shutdown
    }
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
