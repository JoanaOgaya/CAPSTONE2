# Pull Request: Phase 22 Revision 5 Corrections (`phase-22-rev5-corrections`)

## 1. Needs Human Decision (Open Questions for Finance)

### A. Adjusting Entries Hierarchy (`FIN-23` / `R4.11 q4`)
In the current implementation:
* Adjusting entries are created **exclusively by the Finance Manager** (`ROLE-001`, EMP-001).
* Approvals are performed **exclusively by the Head Accountant** (`ROLE-002`, EMP-002) with creator self-approval strictly forbidden (`403 Forbidden`).
* There is no verification step (`ValidateAdjustment` returns HTTP 400); entries approve directly from `Pending` and immediately post balanced journal entries to the General Ledger and alter invoice balances.

**Open Question for Finance**:
1. Confirm whether Head Accountant approval of Finance Manager adjusting entries satisfies external audit independence, or if adjusting entries should instead be recorded by Staff/Head Accountant and approved by Finance Manager.
2. Confirm whether CEO/President approval should be required when the Finance Manager is the creator.
3. Confirm whether a monetary threshold should apply to adjusting entries, and if so, what amount and which approver.

### B. Invoice Approval Workflow Alignment
* **Intended Workflow**: Accountant records first &rarr; Assistant Finance Manager approves &rarr; large amounts also require Finance Manager approval.
* **Current Codebase Implementation**:
  1. Assistant Finance Manager (`EMP-004`) is strictly forbidden with HTTP 403 from all invoice operations (`InvoicesController.cs:1002-1005`, `Invoice.cs:216, 280`, `InvoiceApprovalWorkflowAndTransitionsTests.cs:382-414`).
  2. Head Accountant (`EMP-002`) verifies and approves invoices (with Finance Manager `EMP-001` stepping in only if Head Accountant was the creator to prevent deadlock).
  3. The transition path in `InvoiceDocumentStatus.cs` is `Draft` &rarr; `For Review` &rarr; `Approved`. Calling either the `verify` action (`InvoicesController.cs:1024`, `Invoice.cs:214-238`) or `approve` action (`InvoicesController.cs:1047`, `Invoice.cs:278-298`) auto-advances the document status directly to `Approved`; `Verified` is not a distinct document state.
  4. Invoices do not have an amount-based escalation tier requiring Finance Manager approval; amounts exceeding ₱500,000 only set an informational audit flag (`IsSuspect = true`, `InvoicesController.cs:703-706`).

**Open Question for Finance**:
Confirm whether to retain the current Head Accountant verification model or refactor the invoice workflow to permit Assistant Finance Manager approvals and introduce Finance Manager threshold escalation.

---

## 2. Test Suite Counts (Fresh Independent Execution)

| Suite | Target | Result | Command Output / Execution Time |
| :--- | :--- | :--- | :--- |
| **Backend** | `foms-backend/FOMS.Tests/FOMS.Tests.csproj` | **531 passed, 0 failed, 0 skipped** | `Passed! - Failed: 0, Passed: 531, Skipped: 0, Total: 531, Duration: 23 s - FOMS.Tests.dll (net9.0)` |
| **Frontend** | `foms-frontend` | **18 test files passed (18), 177 tests passed (177)** | `Test Files 18 passed (18), Tests 177 passed (177), Duration 33.89s` (`npm test`) |
| **AI Gateway** | `ai-gateway.Tests/foms-ai-gateway.Tests.csproj` | **40 passed, 0 failed, 0 skipped** | `Passed! - Failed: 0, Passed: 40, Skipped: 0, Total: 40, Duration: 2 s - foms-ai-gateway.Tests.dll` |
| **AI Service** | `ai-service` | **31 passed, 0 failed** | `31 passed in 11.97s` (`python -m pytest -v`) |

---

## 3. Deploy Notes

* **Azure Web App CI/CD (`.github/workflows/develop_fomsintegration.yml`)**:
  * Triggers **only on push to branch `develop`**.
  * Merging this branch into `main` will **NOT** trigger an automatic Azure deployment via GitHub Actions.
* **Vercel Frontend**:
  * `vercel.json` is configured in the repository root and `foms-frontend/vercel.json` for SPA routing.
  * Deployment behavior depends on Vercel project dashboard settings. If the GitHub repository is connected to Vercel with default settings, merging to `main` will trigger a production deployment, while pushes to feature branches trigger preview deployments. Dashboard settings must be verified before merging.

---

## 4. Known Limits

* **R4.9 Immutability Triggers**: Immutability triggers are documented for audit reporting only and are not installed in the database; application-level status guards enforce record immutability.
* **Database Migrations**: No new EF Core database migrations were generated in this branch; all fixes utilize existing schemas and entity configurations.

---

## 5. Commit Summary & PR Base Confirmation

Depending on which base branch the Pull Request targets, the commit count is:
* **Target `phase-21-rev5-gaps` (`eebeec1`)**: 34 commits (isolated to Phase 22 corrections).
* **Target `main` (`32b2601`)**: 67 commits (cumulative changes across all sequential phases since Phase 6/7 hardening merge).

### Commits on `phase-22-rev5-corrections` since `phase-21-rev5-gaps` (`eebeec1`):

* `391f506` - `test(invoices): add approve route discount gate test and isolate threshold changes across verify and approve`
* `146252c` - `docs/test: rename discount transition test and align DECISIONS.md description to verify route`
* `669b316` - `docs: clean adjusting entry threshold question, clarify invoice transitions, and note PR base options`
* `b918b8f` - `docs: add comprehensive PR description for phase-22-rev5-corrections`
* `24690de` - `ci: add frontend test workflow for foms-frontend on pull_request and push`
* `20ac6ea` - `docs(decisions): update invoice workflow comparison and add open questions for Finance`
* `1814bb4` - `docs: add open question for Finance on adjusting entry approver; test discount transition re-checks across threshold changes`
* `dd46eda` - `docs: log R4.4 status deviation, posted bit schema notes, and update bypass route status`
* `a4e5d84` - `test(invoices): add real controller integration tests for CEO approval gate on invoice discounts`
* `bec73c6` - `fix(disbursements): evaluate CSH-09 threshold against request creation date in RequestCashAdvanceAsync; add historical dating tests`
* `52a264c` - `fix(adjustments): HA ValidateAdjustment returns 400 without mutating balances; add end-to-end real controller tests`
* `0c59e9c` - `test(frontend): assert Finance Manager cannot approve adjustments on Page 12`
* `93389fa` - `docs(decisions): adjustments pass corrections and evidence citations`
* `9b5cddf` - `fix(frontend/adjustments): restrict Approve action to Head Accountant only`
* `495cff9` - `fix(adjustments): redirect FM and HA bypass routes to ApproveAdjustmentCommandHandler`
* `aa87f4a` - `fix(adjustments): use Manila dates in AdjustmentFeatures; add TCADJ001-008 governance tests`
* `c523f2c` - `docs(decisions): document closure of R5.11 item 1 correction-record bypass in commit daf08e2`
* `87f9521` - `feat(frontend): show Correction label on corrected records and test Head Accountant recording confirmation`
* `daf08e2` - `fix(ceo-approvals): close correction bypass for budget transfers and carry-overs per R5.11 item 1`
* `3a6c883` - `docs(decisions): mark R5.11 item 1 as PROPOSED - IMPLEMENTED with file citations and test names`
* `df306cb` - `test(phase-22): add comprehensive real service tests for R5.11 item 1 maker-checker and frontend role checks`
* `3b97b75` - `feat(frontend): add Head Accountant maker-checker CEO approval recording confirmation UI and role access`
* `ad11057` - `feat(ceo-approvals): implement R5.11 item 1 maker-checker recording confirmation, status guard, idempotency and audit logs`
* `077beec` - `test(phase-22): add real service invocation tests and closed month adjustment tests`
* `c99af81` - `fix(fin-23): evaluate adjustment date instead of today in closed accounting period check`
* `82b96c8` - `docs(decisions): mark r5.11 item 1 as PROPOSED - NOT IMPLEMENTED`
* `99b9a30` - `test(phase-22): add block tests for items 2,3,4,5,7, closed period tests, SoD tests, and Manila boundary clock tests`
* `cb7da3c` - `fix(fin-23): add closed period check, enforce Head Accountant approver role and creator SoD on adjustments`
* `7c1e810` - `fix(gate): add injectable clock to CeoApprovalGateService for Manila timezone boundary handling`
* `6714211` - `docs(audit): update r5.11 status wording, correct item 7 spec wording, and document fin-23 approver role`
* `11bc335` - `docs(audit): trace department rules to FIN-26 and log FIN-23 status deviation`
* `95e0921` - `fix(phase-22): revert item 8 attachment to optional, restrict fin-23 to fm, add unset threshold go-live step, and trace iam-11/12`
* `39c9d41` - `feat(phase-22): correct rev5 findings, po decisions c and e, 8-item matrix, and 390px check`
