import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ErpErrorReportingService } from '../services/erp-error-reporting.service';
import { SafeErrorResponse } from '../models';

export const erpErrorHttpInterceptor: HttpInterceptorFn = (req, next) => {
  const reporter = inject(ErpErrorReportingService);
  return next(req).pipe(
    catchError((error) => {
      if (error instanceof HttpErrorResponse) {
        const body = error.error as SafeErrorResponse | null;
        if (body?.errorReference) {
          reporter.attachExistingReference(body);
        } else {
          reporter.reportHttpError(error, req.url);
        }
      }
      return throwError(() => error);
    })
  );
};
