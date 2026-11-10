# DMS Team Handoff Specification

## Overview
This document specifies the integration requirements and schema contracts for the external Delivery Management System (DMS) application interfacing with FOMS (Finance Operations Management System).

---

## 1. DMS Add New Employee Form Requirements (R5.11 item 4, R5.11 item 7)
- **Position Selection**:
  - Non-finance positions must be chosen from the existing distinct positions already registered in DMS (e.g. Courier, Dispatcher, Driver, Hub Supervisor, Sorter).
  - Do not invent arbitrary position names.
- **Operational Roles**:
  - Remove the pre-selected `Encoder` default.
  - Require an explicit selection of operational role by the user.
  - Provide an explicit option for `None (no DMS access)`.
- **Client (Consignor) Option**:
  - Retain the `Client (Consignor / Order Creator)` role only if DMS actively registers consignor portal users. Omit from internal operations workflows.

---

## 2. Shared Integration Endpoints
- **Waybills / Proof of Delivery (POD)**:
  - `POST /api/integration/dms/delivery-completed`
  - `POST /api/integration/dms/delivery-failed`
  - `POST /api/integration/dms/redelivery-completed`
- **Cash Advances & Post-Trip Liquidations**:
  - `POST /api/integration/dms/cash-advance-request`
  - `POST /api/integration/dms/liquidation-submission`

---

## 3. Security & Identification
- All requests must include the shared integration secret key (`DMS_TO_FOMS_KEY` in header `X-DMS-Key`).
- Employee identification across systems must strictly match the canonical `EMP-###` format (e.g. `EMP-001` through `EMP-004`).
