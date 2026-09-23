import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { ERP_CORRELATION_ID, ERP_ERROR_CONFIG } from '../tokens';

function generateId(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
    return crypto.randomUUID().replace(/-/g, '');
  }
  return Math.random().toString(36).slice(2) + Date.now().toString(36);
}

export const erpCorrelationInterceptor: HttpInterceptorFn = (req, next) => {
  const config = inject(ERP_ERROR_CONFIG);
  const correlation = inject(ERP_CORRELATION_ID);
  const header = config.correlationHeaderName ?? 'X-Correlation-ID';

  const existing = req.headers.get(header) ?? correlation.value ?? generateId();
  correlation.value = existing;

  const request = req.headers.has(header)
    ? req
    : req.clone({ headers: req.headers.set(header, existing) });

  return next(request);
};
