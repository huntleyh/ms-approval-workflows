# Dataverse Schema — Approvals Demo (Option B)

## Tables

### 1. cr_workflowdefinition — Workflow Definitions
| Column | Type | Notes |
|---|---|---|
| cr_workflowdefinition_name | String(255) | Primary name |
| cr_documenttype | String(255) | E.g. "Vendor Contract" |
| cr_description | String(2000) | |
| cr_active | Boolean | Only active workflows appear in Submit dropdown |

### 2. cr_workflowtier — Workflow Tiers
| Column | Type | Notes |
|---|---|---|
| cr_workflowtier_name | String(255) | Primary name |
| cr_workflowdefinitionid | String(100) | FK to WorkflowDefinition (GUID) |
| cr_tiernumber | Integer | 1-based, determines sequence |
| cr_tiername | String(255) | Display name, e.g. "Manager Review" |
| cr_approveremails | String(1000) | Semicolon-separated email list |
| cr_approvertype | Integer | 1=Internal, 2=External |
| cr_requiredapprovals | Integer | Default 1 |
| cr_reminderhours | Integer | Hours before sending reminder |
| cr_expiryhours | Integer | Hours until tier expires |

### 3. cr_approvalrequest — Approval Requests
| Column | Type | Notes |
|---|---|---|
| cr_approvalrequest_name | String(255) | Primary name / title |
| cr_requestedby | String(255) | |
| cr_workflowdefinitionid | String(100) | FK |
| cr_workflowdefinitionname | String(255) | Denormalized for display |
| cr_status | Integer | 1=Pending,2=Approved,3=Rejected,4=Reset,5=Expired |
| cr_currenttiernumber | Integer | Active tier |
| cr_logicapprunid | String(255) | LA-1 run ID for cancellation |
| cr_logicapprunurl | String(1000) | Full management API URL |
| cr_documenttitle | String(255) | |
| cr_documentsummary | String(2000) | |
| cr_documentcontent | String(8192) | Full document text |
| cr_resetcount | Integer | Number of times reset |

### 4. cr_approvaltier_progress — Tier Progress
| Column | Type | Notes |
|---|---|---|
| cr_approvaltier_progress_name | String(255) | Primary name |
| cr_approvalrequestid | String(100) | FK |
| cr_workflowtierid | String(100) | FK |
| cr_tiernumber | Integer | |
| cr_tiername | String(255) | |
| cr_status | Integer | 1=Waiting,2=Pending,3=Approved,4=Rejected,5=Expired,6=Skipped |
| cr_assignedapprover | String(255) | Current approver email |
| cr_approvaltokenhash | String(255) | SHA-256 of one-time token |
| cr_tokenconsumed | Boolean | |
| cr_callbackurl | String(2000) | LA-1 suspend/resume URL |
| cr_decision | Integer | 1=Pending,2=Approved,3=Rejected |
| cr_approvercomments | String(2000) | |
| cr_delegatedfrom | String(255) | Original approver email if delegated |
| cr_lastreminderat | DateTime | Last reminder timestamp |
| cr_decisiondate | DateTime | When decision was made |

### 5. cr_approvalauditlog — Audit Log
| Column | Type | Notes |
|---|---|---|
| cr_approvalauditlog_name | String(255) | Primary name |
| cr_approvalrequestid | String(100) | FK |
| cr_tiernumber | Integer | 0 = request-level event |
| cr_eventtype | Integer | See AuditEventType enum |
| cr_performedby | String(255) | Email or "system" |
| cr_previousvalue | String(4000) | JSON snapshot |
| cr_newvalue | String(4000) | JSON snapshot |
| cr_ipaddress | String(100) | |
| cr_notes | String(2000) | |

## AuditEventType Values
1=Submitted, 2=TierStarted, 3=Notified, 4=ReminderSent, 5=PageVisited,
6=CallbackReceived, 7=Approved, 8=Rejected, 9=Delegated, 10=TierExpired,
11=RequestExpired, 12=Reset, 13=FlowResumed, 14=FlowCompleted, 15=TokenConsumed
