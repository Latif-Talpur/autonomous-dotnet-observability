import { ErrorHandler, EnvironmentProviders, makeEnvironmentProviders } from '@angular/core';
import { ErpErrorManagementConfig } from './models';
import { ERP_CORRELATION_ID, ERP_ERROR_CONFIG } from './tokens';
import { ErpErrorHandler } from './error-handler/erp-error-handler';

export function provideErpErrorManagement(config: ErpErrorManagementConfig): EnvironmentProviders {
  const normalized: ErpErrorManagementConfig = {
    correlationHeaderName: 'X-Correlation-ID',
    enableIssueReporting: true,
    captureUserAgent: true,
    ...config,
  };
  return makeEnvironmentProviders([
    { provide: ERP_ERROR_CONFIG, useValue: normalized },
    { provide: ERP_CORRELATION_ID, useValue: { value: null } },
    { provide: ErrorHandler, useClass: ErpErrorHandler },
  ]);
}
