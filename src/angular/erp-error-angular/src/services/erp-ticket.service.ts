import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ERP_ERROR_CONFIG } from '../tokens';

export interface CreateTicketRequest {
  errorReference: string;
  description?: string;
  reportedBy: string;
  priority?: number;
  queueCode?: string;
  severityCode?: string;
}

export interface TicketView {
  ticketId: string;
  ticketNumber: string;
  statusCode: string;
  statusName: string;
  priority: number;
  openedAtUtc: string;
  errorReference: string;
}

@Injectable({ providedIn: 'root' })
export class ErpTicketService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ERP_ERROR_CONFIG);

  create(request: CreateTicketRequest): Observable<TicketView> {
    const url = this.config.ticketEndpoint ?? '/api/tickets';
    return this.http.post<TicketView>(url, request);
  }

  get(ticketId: string): Observable<TicketView> {
    const url = (this.config.ticketEndpoint ?? '/api/tickets') + '/' + encodeURIComponent(ticketId);
    return this.http.get<TicketView>(url);
  }
}
