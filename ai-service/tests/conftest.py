import os
import pytest

# Ensure testing environment is configured before application imports
os.environ["FOMS_ENV"] = "testing"

from app.models.database import Base, engine

@pytest.fixture(autouse=True, scope="session")
def setup_test_db():
    Base.metadata.create_all(bind=engine)
    yield
