import pytest
from datetime import date, timezone, timedelta
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker
from app.models.database import Base, AICollectionPriority, AIRecommendation
from app.services.collection import calculate_collection_priorities, FOMS_TIMEZONE

@pytest.fixture
def db_session():
    engine = create_engine("sqlite:///:memory:")
    TestingSessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)
    Base.metadata.create_all(bind=engine)
    db = TestingSessionLocal()
    try:
        yield db
    finally:
        db.close()


def test_clock_not_yet_due(db_session):
    """Invoice due in the future (10 days ahead) must be classified as LOW / Not Due."""
    clock = date(2026, 7, 10)
    due_date = "2026-07-20"
    ar_data = [{
        "invoiceId": "INV-FUTURE",
        "clientId": "C-1",
        "amount": 15000.0,
        "dueDate": due_date,
    }]

    processed, _ = calculate_collection_priorities(db_session, ar_data, [], current_date=clock)
    assert processed == 1

    prio = db_session.query(AICollectionPriority).filter_by(invoice_id="INV-FUTURE").first()
    assert prio.priority == "LOW"
    assert "not yet due" in prio.explanation.lower()
    assert "10 days remaining" in prio.explanation


def test_clock_due_today(db_session):
    """Invoice due exactly today (0 days overdue) must be classified as MEDIUM / Due Soon."""
    clock = date(2026, 7, 20)
    due_date = "2026-07-20"
    ar_data = [{
        "invoiceId": "INV-TODAY",
        "clientId": "C-2",
        "amount": 25000.0,
        "dueDate": due_date,
    }]

    processed, _ = calculate_collection_priorities(db_session, ar_data, [], current_date=clock)
    assert processed == 1

    prio = db_session.query(AICollectionPriority).filter_by(invoice_id="INV-TODAY").first()
    assert prio.priority == "MEDIUM"
    assert "due soon" in prio.explanation.lower()
    assert "0 days left" in prio.explanation


def test_clock_one_day_overdue(db_session):
    """Invoice due yesterday (1 day overdue) must be classified as HIGH / Overdue."""
    clock = date(2026, 7, 21)
    due_date = "2026-07-20"
    ar_data = [{
        "invoiceId": "INV-1DAY",
        "clientId": "C-3",
        "amount": 20000.0,
        "dueDate": due_date,
    }]

    processed, _ = calculate_collection_priorities(db_session, ar_data, [], current_date=clock)
    assert processed == 1

    prio = db_session.query(AICollectionPriority).filter_by(invoice_id="INV-1DAY").first()
    assert prio.priority == "HIGH"
    assert "overdue by 1 days" in prio.explanation.lower()


def test_clock_thirty_plus_days_overdue(db_session):
    """Invoice 35 days overdue with high balance (>= 50k) must be classified as URGENT."""
    clock = date(2026, 7, 20)
    due_date = "2026-06-15"  # 35 days overdue
    ar_data = [{
        "invoiceId": "INV-35DAYS",
        "clientId": "C-4",
        "amount": 75000.0,
        "outstandingBalance": 75000.0,
        "dueDate": due_date,
    }]

    processed, _ = calculate_collection_priorities(db_session, ar_data, [], current_date=clock)
    assert processed == 1

    prio = db_session.query(AICollectionPriority).filter_by(invoice_id="INV-35DAYS").first()
    assert prio.priority == "URGENT"
    assert "severely overdue by 35 days" in prio.explanation.lower()


def test_clock_month_boundary(db_session):
    """Tests date calculation across month boundary: due Feb 28, evaluated on March 1 (1 day overdue)."""
    clock = date(2026, 3, 1)
    due_date = "2026-02-28"
    ar_data = [{
        "invoiceId": "INV-MONTH-BOUND",
        "clientId": "C-5",
        "amount": 10000.0,
        "dueDate": due_date,
    }]

    processed, _ = calculate_collection_priorities(db_session, ar_data, [], current_date=clock)
    assert processed == 1

    prio = db_session.query(AICollectionPriority).filter_by(invoice_id="INV-MONTH-BOUND").first()
    assert prio.priority == "HIGH"
    assert "overdue by 1 days" in prio.explanation.lower()


def test_clock_year_boundary(db_session):
    """Tests date calculation across year boundary: due Dec 31 2025, evaluated on Jan 2 2026 (2 days overdue)."""
    clock = date(2026, 1, 2)
    due_date = "2025-12-31"
    ar_data = [{
        "invoiceId": "INV-YEAR-BOUND",
        "clientId": "C-6",
        "amount": 30000.0,
        "dueDate": due_date,
    }]

    processed, _ = calculate_collection_priorities(db_session, ar_data, [], current_date=clock)
    assert processed == 1

    prio = db_session.query(AICollectionPriority).filter_by(invoice_id="INV-YEAR-BOUND").first()
    assert prio.priority == "HIGH"
    assert "overdue by 2 days" in prio.explanation.lower()
