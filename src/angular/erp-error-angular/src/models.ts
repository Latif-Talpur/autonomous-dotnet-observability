export interface ErpErrorManagementConfig {
  applicationName: string;
  environment: string;
  applicationVersion?: string;
  clientErrorEndpoint: string;
  ticketEndpoint?: string;
  enableIssueReporting?: boolean;
  correlationHeaderName?: string;
  captureUserAgent?: boolean;
}

export interface SafeErrorResponse {
  type?: string;
  title?: string;
  status?: number;
  errorReference: string;
  correlationId: string;
  canReportIssue?: boolean;
}

export interface ErrorEnvelopePayload {
  message?: string;
  exceptionType?: string;
  stackTrace?: string;
  module?: string;
  screen?: string;
  component?: string;
  url?: string;
  browser?: string;
  clientVersion?: string;
  device?: string;
  applicationVersion?: string;
  userId?: string;
  httpStatus?: number;
  occurredAtUtc?: string;
  applicationCode?: string;
  environmentCode?: string;
  layer?: string;
  correlationId?: string;
}
