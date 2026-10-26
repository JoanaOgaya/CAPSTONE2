-- Seed sample approval requests and history into Azure SQL Database
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-1D347C')
INSERT INTO dbo.CashOutflowApprovalRequests (request_guid, cash_outflow_id, transaction_reference, payee, category, purpose, amount, currency, approval_level, assigned_role, status, is_escalated, escalation_threshold, submitted_by, submitted_by_id, rejection_reason, remarks, is_posted)
VALUES ('CO-20261021-1D347C', 'CO-20261021-1D347C', 'CO-TEST-2851', 'National Book Store', 'Office Supplies', 'Quarterly printer paper and toner cartridges', 4500.00, 'PHP', 'Level 1', 'Assistant Financial Manager', 'Pending Assistant Review', 0, 10000.00, 'EMP-004', 'EMP-004', NULL, NULL, 0);

IF NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-49BEB2')
INSERT INTO dbo.CashOutflowApprovalRequests (request_guid, cash_outflow_id, transaction_reference, payee, category, purpose, amount, currency, approval_level, assigned_role, status, is_escalated, escalation_threshold, submitted_by, submitted_by_id, rejection_reason, remarks, is_posted)
VALUES ('CO-20261021-49BEB2', 'CO-20261021-49BEB2', 'CO-TEST-8383', 'National Book Store', 'Office Supplies', 'Quarterly printer paper and toner cartridges', 4500.00, 'PHP', 'Level 1', 'Assistant Financial Manager', 'Approved by Assistant', 0, 10000.00, 'EMP-004', 'EMP-004', NULL, NULL, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-35C176')
INSERT INTO dbo.CashOutflowApprovalRequests (request_guid, cash_outflow_id, transaction_reference, payee, category, purpose, amount, currency, approval_level, assigned_role, status, is_escalated, escalation_threshold, submitted_by, submitted_by_id, rejection_reason, remarks, is_posted)
VALUES ('CO-20261021-35C176', 'CO-20261021-35C176', 'CO-HIGH-4014', 'Dell Systems Philippines', 'Equipment Purchase', 'Server rack replacement unit', 25000.00, 'PHP', 'Level 2', 'Financial Manager', 'Approved by Financial Manager', 1, 10000.00, 'EMP-004', 'EMP-004', NULL, NULL, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-C4B425')
INSERT INTO dbo.CashOutflowApprovalRequests (request_guid, cash_outflow_id, transaction_reference, payee, category, purpose, amount, currency, approval_level, assigned_role, status, is_escalated, escalation_threshold, submitted_by, submitted_by_id, rejection_reason, remarks, is_posted)
VALUES ('CO-20261021-C4B425', 'CO-20261021-C4B425', 'CO-TEST-2980', 'National Book Store', 'Office Supplies', 'Quarterly printer paper and toner cartridges', 4500.00, 'PHP', 'Level 1', 'Assistant Financial Manager', 'Approved by Assistant', 0, 10000.00, 'EMP-004', 'EMP-004', NULL, NULL, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-3427A9')
INSERT INTO dbo.CashOutflowApprovalRequests (request_guid, cash_outflow_id, transaction_reference, payee, category, purpose, amount, currency, approval_level, assigned_role, status, is_escalated, escalation_threshold, submitted_by, submitted_by_id, rejection_reason, remarks, is_posted)
VALUES ('CO-20261021-3427A9', 'CO-20261021-3427A9', 'CO-HIGH-1921', 'Dell Systems Philippines', 'Equipment Purchase', 'Server rack replacement unit', 25000.00, 'PHP', 'Level 2', 'Financial Manager', 'Approved by Financial Manager', 1, 10000.00, 'EMP-004', 'EMP-004', NULL, NULL, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-6FEAE9')
INSERT INTO dbo.CashOutflowApprovalRequests (request_guid, cash_outflow_id, transaction_reference, payee, category, purpose, amount, currency, approval_level, assigned_role, status, is_escalated, escalation_threshold, submitted_by, submitted_by_id, rejection_reason, remarks, is_posted)
VALUES ('CO-20261021-6FEAE9', 'CO-20261021-6FEAE9', 'CO-REJ-9445', 'Unverified Vendor', 'Equipment Purchase', 'Unbudgeted item', 18000.00, 'PHP', 'Level 2', 'Financial Manager', 'Rejected by Financial Manager', 1, 10000.00, 'EMP-004', 'EMP-004', 'Budget not approved for this fiscal quarter.', NULL, 0);

IF NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-BDF390')
INSERT INTO dbo.CashOutflowApprovalRequests (request_guid, cash_outflow_id, transaction_reference, payee, category, purpose, amount, currency, approval_level, assigned_role, status, is_escalated, escalation_threshold, submitted_by, submitted_by_id, rejection_reason, remarks, is_posted)
VALUES ('CO-20261021-BDF390', 'CO-20261021-BDF390', 'CO-RET-6515', 'Airline Booking', 'Travel Expense', 'Conference attendance', 15000.00, 'PHP', 'Level 2', 'Financial Manager', 'Returned for Correction', 1, 10000.00, 'EMP-004', 'EMP-004', NULL, 'Please attach official receipt and travel authorization slip.', 0);

-- Also add sample history
DECLARE @req3 INT = (SELECT approval_request_id FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-35C176');
IF @req3 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalHistory WHERE approval_request_id = @req3)
BEGIN
    INSERT INTO dbo.CashOutflowApprovalHistory (approval_request_id, actor_user_id, actor_name, actor_role, action, previous_status, new_status, remarks)
    VALUES 
    (@req3, 'EMP-004', 'EMP-004', 'Assistant of Financial Manager', 'Submitted', 'None', 'Pending Assistant Review', 'Submitted. Amount 25,000.00 exceeds Assistant threshold 10,000.00 (Flagged for Level 2 review).'),
    (@req3, 'EMP-004', 'EMP-004', 'Assistant of Financial Manager', 'Escalated', 'Pending Assistant Review', 'Pending Financial Manager Approval', 'Exceeds Assistant authorization limit. Endorsed for Financial Manager approval.'),
    (@req3, 'EMP-001', 'EMP-001', 'Financial Manager', 'Approved', 'Pending Financial Manager Approval', 'Approved by Financial Manager', 'Approved by Financial Manager. Cash outflow posted.');
END;

DECLARE @req5 INT = (SELECT approval_request_id FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-3427A9');
IF @req5 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalHistory WHERE approval_request_id = @req5)
BEGIN
    INSERT INTO dbo.CashOutflowApprovalHistory (approval_request_id, actor_user_id, actor_name, actor_role, action, previous_status, new_status, remarks)
    VALUES 
    (@req5, 'EMP-004', 'EMP-004', 'Assistant of Financial Manager', 'Submitted', 'None', 'Pending Assistant Review', 'Submitted. Amount 25,000.00 exceeds Assistant threshold 10,000.00 (Flagged for Level 2 review).'),
    (@req5, 'EMP-004', 'EMP-004', 'Assistant of Financial Manager', 'Escalated', 'Pending Assistant Review', 'Pending Financial Manager Approval', 'Exceeds Assistant authorization limit. Endorsed for Financial Manager approval.'),
    (@req5, 'EMP-001', 'EMP-001', 'Financial Manager', 'Approved', 'Pending Financial Manager Approval', 'Approved by Financial Manager', 'Approved by Financial Manager. Cash outflow posted.');
END;

DECLARE @req6 INT = (SELECT approval_request_id FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-6FEAE9');
IF @req6 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalHistory WHERE approval_request_id = @req6)
BEGIN
    INSERT INTO dbo.CashOutflowApprovalHistory (approval_request_id, actor_user_id, actor_name, actor_role, action, previous_status, new_status, remarks)
    VALUES 
    (@req6, 'EMP-004', 'EMP-004', 'Assistant of Financial Manager', 'Submitted', 'None', 'Pending Assistant Review', 'Submitted. Amount 18,000.00 exceeds Assistant threshold 10,000.00 (Flagged for Level 2 review).'),
    (@req6, 'EMP-004', 'EMP-004', 'Assistant of Financial Manager', 'Escalated', 'Pending Assistant Review', 'Pending Financial Manager Approval', 'Exceeds Assistant authorization limit. Endorsed for Financial Manager approval.'),
    (@req6, 'EMP-001', 'EMP-001', 'Financial Manager', 'Rejected', 'Pending Financial Manager Approval', 'Rejected by Financial Manager', 'Budget not approved for this fiscal quarter.');
END;

DECLARE @req7 INT = (SELECT approval_request_id FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-BDF390');
IF @req7 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalHistory WHERE approval_request_id = @req7)
BEGIN
    INSERT INTO dbo.CashOutflowApprovalHistory (approval_request_id, actor_user_id, actor_name, actor_role, action, previous_status, new_status, remarks)
    VALUES 
    (@req7, 'EMP-004', 'EMP-004', 'Assistant of Financial Manager', 'Submitted', 'None', 'Pending Assistant Review', 'Submitted. Amount 15,000.00 exceeds Assistant threshold 10,000.00 (Flagged for Level 2 review).'),
    (@req7, 'EMP-004', 'EMP-004', 'Assistant of Financial Manager', 'Escalated', 'Pending Assistant Review', 'Pending Financial Manager Approval', 'Exceeds Assistant authorization limit. Endorsed for Financial Manager approval.'),
    (@req7, 'EMP-001', 'EMP-001', 'Financial Manager', 'Returned', 'Pending Financial Manager Approval', 'Returned for Correction', 'Please attach official receipt and travel authorization slip.');
END;
