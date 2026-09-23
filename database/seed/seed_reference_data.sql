-- Reference data seed for ERP Error Management.
-- Idempotent: uses INSERT OR IGNORE. Run after V001__initial_schema.sql.
PRAGMA foreign_keys = ON;
BEGIN TRANSACTION;

INSERT OR IGNORE INTO environment(environment_id, code, name) VALUES
    ('env-dev', 'DEV', 'Development'),
    ('env-test', 'TEST', 'Test'),
    ('env-uat', 'UAT', 'User Acceptance'),
    ('env-prod', 'PROD', 'Production');

INSERT OR IGNORE INTO severity(severity_id, code, name, rank) VALUES
    ('sev-critical', 'CRITICAL', 'Critical', 1),
    ('sev-high',     'HIGH',     'High',     2),
    ('sev-medium',   'MEDIUM',   'Medium',   3),
    ('sev-low',      'LOW',      'Low',      4),
    ('sev-info',     'INFO',     'Informational', 5);

INSERT OR IGNORE INTO error_category(category_id, code, name, default_severity_id) VALUES
    ('cat-validation',   'VALIDATION',    'Validation',      'sev-low'),
    ('cat-authorization','AUTHORIZATION', 'Authorization',   'sev-medium'),
    ('cat-database',     'DATABASE',      'Database',        'sev-high'),
    ('cat-integration',  'INTEGRATION',   'Integration',     'sev-high'),
    ('cat-business',     'BUSINESS',      'Business rule',   'sev-medium'),
    ('cat-system',       'SYSTEM',        'Unexpected system','sev-critical');

INSERT OR IGNORE INTO support_queue(queue_id, code, name, is_active) VALUES
    ('queue-l1',       'L1',       'Level 1 Support', 1),
    ('queue-l2',       'L2',       'Level 2 Engineering', 1),
    ('queue-database', 'DATABASE', 'Database Team', 1),
    ('queue-infra',    'INFRA',    'Infrastructure', 1);

INSERT OR IGNORE INTO ticket_status(status_id, code, name, display_order, is_terminal) VALUES
    ('status-new',        'NEW',         'New',         10, 0),
    ('status-triage',     'TRIAGE',      'In Triage',   20, 0),
    ('status-assigned',   'ASSIGNED',    'Assigned',    30, 0),
    ('status-investigating','INVESTIGATING','Investigating',40, 0),
    ('status-blocked',    'BLOCKED',     'Blocked',     50, 0),
    ('status-resolved',   'RESOLVED',    'Resolved',    80, 0),
    ('status-closed',     'CLOSED',      'Closed',      90, 1),
    ('status-rejected',   'REJECTED',    'Rejected',    95, 1),
    ('status-reopened',   'REOPENED',    'Reopened',    35, 0);

INSERT OR IGNORE INTO sla_policy(sla_policy_id, queue_id, severity_id, response_minutes, resolution_minutes, is_active) VALUES
    ('sla-l1-critical', 'queue-l1', 'sev-critical', 15, 240, 1),
    ('sla-l1-high',     'queue-l1', 'sev-high',     30, 480, 1),
    ('sla-l1-medium',   'queue-l1', 'sev-medium',   60, 1440, 1),
    ('sla-l1-low',      'queue-l1', 'sev-low',     240, 4320, 1),
    ('sla-l2-critical', 'queue-l2', 'sev-critical', 30, 480, 1),
    ('sla-l2-high',     'queue-l2', 'sev-high',     60, 960, 1);

INSERT OR IGNORE INTO application(application_id, code, name, is_active) VALUES
    ('app-erp', 'ERP', 'Main ERP', 1);

COMMIT;
