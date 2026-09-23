# @company/erp-error-angular

Angular 20 client for the ERP Error Management framework.

## Install
```bash
npm install @company/erp-error-angular
```

## Register once at bootstrap
```ts
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig } from '@angular/core';
import {
  provideErpErrorManagement,
  erpCorrelationInterceptor,
  erpErrorHttpInterceptor,
} from '@company/erp-error-angular';

export const appConfig: ApplicationConfig = {
  providers: [
    provideHttpClient(
      withInterceptors([erpCorrelationInterceptor, erpErrorHttpInterceptor])
    ),
    provideErpErrorManagement({
      applicationName: 'MainERP',
      environment: 'Production',
      clientErrorEndpoint: '/api/error-management/client-errors',
      ticketEndpoint: '/api/tickets',
    }),
  ],
};
```

## Building
```bash
npm run build
```
