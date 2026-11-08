# FOMS AI Intelligence Service

The AI Intelligence Service provides predictive collection recommendations, duplicate invoice/waybill detection, and OCR extraction.

## Runtime & Environment
- **Python Version:** Python 3.11 (`3.11.x` is required; pre-releases such as 3.14 lack pre-built C-extension binary wheels).
- **Test Runner:** Run tests with `pytest tests` (or `py -3.11 -m pytest tests`). Runs fully offline without a live PostgreSQL instance via automatic in-memory SQLite fallback.

## Database & Migrations

The service stores AI results in PostgreSQL (`foms_ai_results`).

### Migration Workflow (Alembic)

Database schema is managed using **Alembic** migrations instead of runtime `Base.metadata.create_all()`:

1. **Apply Migrations Before Service Launch:**
   ```bash
   alembic upgrade head
   ```
   Or use the runner script:
   ```bash
   python scripts/migrate_ai_db.py
   ```

2. **Generate New Schema Revision:**
   ```bash
   alembic revision --autogenerate -m "describe_changes"
   ```

3. **Downgrade Migration:**
   ```bash
   alembic downgrade -1
   ```

## Security & Configuration

- `AI_SERVICE_API_KEY`: Required authentication key for service-to-service communication with `ai-gateway`.
- `MSSQL_TRUST_SERVER_CERTIFICATE`: Defaults to `no` (strict certificate verification) in production. Set to `yes` only in local development.
