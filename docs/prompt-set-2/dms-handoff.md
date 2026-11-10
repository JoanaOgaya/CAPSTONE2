# DMS Team Handoff Specification

## Overview
This document specifies the integration requirements and schema contracts for the external Delivery Management System (DMS) application interfacing with FOMS (Finance Operations Management System).

> [!NOTE]
> **Audit Status**: Check 9 is marked **UNVERIFIED (handoff doc only)** because DMS is maintained in an external sibling repository outside this workspace. The requirements below are handed off for DMS team implementation.

---

## 1. DMS Add New Employee Form Requirements (R5.11 item 4, R5.11 item 7)
- **Position Selection (R5.11 Item 4)**:
  - Non-finance positions must be chosen strictly from the existing distinct positions already registered in DMS:
    - `Courier`
    - `Dispatcher`
    - `Driver`
    - `Hub Supervisor`
    - `Sorter`
  - Do not invent arbitrary position names.
- **Operational Roles Selection**:
  - **Remove pre-selected `Encoder`**: The DMS operational role dropdown must NOT default to `Encoder`; it must start empty requiring explicit user selection.
  - **Add `None (no DMS access)` Option**: Provide an explicit option for `None (no DMS access)` for employees who do not perform operational dispatch/delivery entries.
  - Allowed DMS operational role options: `Admin`, `Encoder`, `Driver`, `None (no DMS access)`.
- **Open Question R5.11 Item 7: Client (Consignor) Option**:
  - In FOMS: `Client` is strictly forbidden and deleted from all roles, enums, dropdowns, and login paths (`IAM-06`, `IAM-11`).
  - In DMS: If DMS uses `Client` to register consignors / order creators for customer portal tracking, that is an external DMS concern. If DMS does not register consignors through this form, the `Client` option should be removed from DMS as well.

---

## 2. Shared Integration Endpoints
- **Waybills / Proof of Delivery (POD)**:
  - `POST /api/integration/dms/delivery-completed`
  - `POST /api/integration/dms/delivery-failed`
  - `POST /api/integration/dms/redelivery-completed`
- **Cash Advances & Post-Trip Liquidations**:
  - `POST /api/integration/dms/cash-advance-request`
  - `POST /api/integration/dms/liquidation-submission`
- **Department Routing on Inbound Cash Advances**:
  - DMS operational cash advance requests missing department are automatically routed to Dispatch Team (`DEP-DSP`), subject to pre-approval department review by the Finance Manager (`FIN-26`).

---

## 3. Security & Identification
- All requests must include the shared integration secret key (`DMS_TO_FOMS_KEY` in header `X-DMS-Key` or `X-DMS-API-Key`).
- Employee identification across systems must strictly match the canonical `EMP-###` format (e.g. `EMP-001` through `EMP-004`).
