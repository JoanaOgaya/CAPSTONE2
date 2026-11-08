import pytest
from datetime import date
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker
from app.models.database import Base, AICollectionPriority, AIRecommendation
from app.services.collection import calculate_collection_priorities

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

def test_calculate_collection_priorities_urgent(db_session):
    # Injected simulated current date: 2026-07-20
    # Overdue > 30 days and balance >= 50,000 -> URGENT
    simulated_clock = date(2026, 7, 20)
    due_date = "2026-06-15" # 35 days overdue
    ar_data = [{
        "invoiceId": "INV-URG",
        "invoiceNumber": "INV-900",
        "clientId": "C-1",
        "clientName": "Big Corp",
        "amount": 75000.0,
        "outstandingBalance": 75000.0,
        "dueDate": due_date,
        "daysOverdue": 35,
        "agingBucket": "31-60 Days"
    }]
    
    processed, updated = calculate_collection_priorities(
        db_session, ar_data, [], current_date=simulated_clock
    )
    assert processed == 1
    
    priority = db_session.query(AICollectionPriority).first()
    assert priority is not None
    assert priority.priority == "URGENT"
    # Outstanding balance high and overdue > 30
    assert "Outstanding balance is high" in priority.explanation
    assert "overdue" in priority.explanation.lower()

    rec = db_session.query(AIRecommendation).first()
    assert rec is not None
    assert rec.priority_result_id == priority.id
    assert "escalate" in rec.recommendation_text.lower()

def test_calculate_collection_priorities_high(db_session):
    # Overdue but not large amount -> HIGH
    simulated_clock = date(2026, 7, 20)
    due_date = "2026-06-25" # 25 days overdue
    ar_data = [{
        "invoiceId": "INV-HIGH",
        "invoiceNumber": "INV-800",
        "clientId": "C-2",
        "clientName": "Mid Corp",
        "amount": 20000.0,
        "outstandingBalance": 20000.0,
        "dueDate": due_date,
        "daysOverdue": 25,
        "agingBucket": "1-30 Days"
    }]
    
    processed, updated = calculate_collection_priorities(
        db_session, ar_data, [], current_date=simulated_clock
    )
    priority = db_session.query(AICollectionPriority).first()
    assert priority is not None
    assert priority.priority == "HIGH"

def test_calculate_collection_priorities_due_soon(db_session):
    # Due in 5 days (<= 7 days) and small balance -> MEDIUM
    simulated_clock = date(2026, 7, 20)
    due_date = "2026-07-25" 
    ar_data = [{
        "invoiceId": "INV-MED",
        "invoiceNumber": "INV-700",
        "clientId": "C-3",
        "clientName": "Small Corp",
        "amount": 10000.0,
        "outstandingBalance": 10000.0,
        "dueDate": due_date,
        "daysOverdue": 0,
        "agingBucket": "Current"
    }]
    
    processed, updated = calculate_collection_priorities(
        db_session, ar_data, [], current_date=simulated_clock
    )
    priority = db_session.query(AICollectionPriority).first()
    assert priority.priority == "MEDIUM"
    assert "due soon" in priority.explanation.lower()
