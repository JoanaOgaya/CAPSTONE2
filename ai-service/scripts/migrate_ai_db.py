#!/usr/bin/env python3
"""
FOMS AI Intelligence Service — Database Migration Runner
Applies pending schema migrations to PostgreSQL before the service starts.
Usage:
    python scripts/migrate_ai_db.py
"""
import os
import sys
from pathlib import Path

# Add project root to sys.path
ai_root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ai_root))

from app.core.config import settings

def run_migrations():
    print(f"=== FOMS AI Service Migration Runner ===")
    print(f"Target Database: {settings.POSTGRES_DATABASE} on {settings.POSTGRES_HOST}:{settings.POSTGRES_PORT}")
    
    # Check if alembic is installed
    try:
        from alembic.config import Config
        from alembic import command
        
        alembic_ini_path = ai_root / "alembic.ini"
        if not alembic_ini_path.is_file():
            raise FileNotFoundError(f"alembic.ini not found at {alembic_ini_path}")
            
        alembic_cfg = Config(str(alembic_ini_path))
        print("Running Alembic upgrade head...")
        command.upgrade(alembic_cfg, "head")
        print("Schema migrations applied successfully via Alembic.")
        return 0
    except ImportError:
        print("Alembic not installed in current environment. Falling back to SQLAlchemy schema synchronization...")
        try:
            from app.core.database import engine
            from app.models.database import Base
            Base.metadata.create_all(bind=engine)
            print("Schema tables synchronized successfully via SQLAlchemy.")
            return 0
        except Exception as e:
            print(f"FATAL: Database migration failed: {e}", file=sys.stderr)
            return 1
    except Exception as e:
        print(f"FATAL: Migration failed: {e}", file=sys.stderr)
        return 1

if __name__ == "__main__":
    sys.exit(run_migrations())
