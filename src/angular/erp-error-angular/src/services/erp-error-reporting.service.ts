import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ErrorEnvelopePayload, SafeErrorResponse } from '../models';
import { ERP_CORRELATION_ID, ERP_ERROR_CONFIG } from '../tokens';

@Injectable({ providedIn: 'root' })
export class ErpErrorReportingService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ERP_ERROR_CONFIG);
  private readonly correlation = inject(ERP_CORRELATION_ID);
  private lastReference: SafeErrorResponse | null = null;
  private readonly recentEndpoints = new Map<string, number>();

  reportUnhandledError(error: unknown): void {
    const payload = this.buildPayload(error, 'Angular');
    this.send(payload);
  }

  reportHttpError(error: HttpErrorResponse, url: string): void {
    if (this.wasRecent(url)) return;
    const payload = this.buildPayload(error, 'HTTP');
    payload.httpStatus = error.status;
    payload.url = url;
    this.send(payload);
  }

  attachExistingReference(response: SafeErrorResponse): void {
    this.lastReference = response;
  }

  getLastReference(): SafeErrorResponse | null {
    return this.lastReference;
  }

  private buildPayload(error: unknown, layer: string): ErrorEnvelopePayload {
    const message = error instanceof Error ? error.message : String(error);
    const stack = error instanceof Error ? error.stack : undefined;
    const type = error instanceof Error ? error.name : 'UnknownError';

    return {
      message,
      stackTrace: stack,
      exceptionType: type,
      applicationCode: this.config.applicationName,
      environmentCode: this.config.environment,
      applicationVersion: this.config.applicationVersion,
      correlationId: this.correlation.value ?? undefined,
      browser: this.config.captureUserAgent && typeof navigator !== 'undefined' ? navigator.userAgent : undefined,
      occurredAtUtc: new Date().toISOString(),
      layer,
    };
  }

  private async send(payload: ErrorEnvelopePayload): Promise<void> {
    try {
      const response = await firstValueFrom(
        this.http.post<SafeErrorResponse>(this.config.clientErrorEndpoint, payload)
      );
      if (response?.errorReference) {
        this.lastReference = response;
      }
    } catch {
      // Never let telemetry throw into the UI.
    }
  }

  private wasRecent(url: string): boolean {
    const now = Date.now();
    for (const [key, ts] of this.recentEndpoints) {
      if (now - ts > 60_000) this.recentEndpoints.delete(key);
    }
    if (this.recentEndpoints.has(url)) return true;
    this.recentEndpoints.set(url, now);
    return false;
  }
}
