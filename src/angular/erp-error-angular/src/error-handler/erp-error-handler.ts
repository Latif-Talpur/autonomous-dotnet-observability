import { ErrorHandler, Injectable, inject } from '@angular/core';
import { ErpErrorReportingService } from '../services/erp-error-reporting.service';

@Injectable()
export class ErpErrorHandler implements ErrorHandler {
  private readonly reporter = inject(ErpErrorReportingService);

  handleError(error: unknown): void {
    try {
      this.reporter.reportUnhandledError(error);
    } finally {
      if (typeof console !== 'undefined') {
        console.error(error);
      }
    }
  }
}
