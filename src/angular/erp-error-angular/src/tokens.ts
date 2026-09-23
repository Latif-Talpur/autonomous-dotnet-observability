import { InjectionToken } from '@angular/core';
import { ErpErrorManagementConfig } from './models';

export const ERP_ERROR_CONFIG = new InjectionToken<ErpErrorManagementConfig>('ERP_ERROR_CONFIG');
export const ERP_CORRELATION_ID = new InjectionToken<{ value: string | null }>('ERP_CORRELATION_ID');
