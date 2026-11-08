from datetime import datetime, date, timezone, timedelta
from typing import List, Dict, Tuple, Optional, Any
from sqlalchemy.orm import Session
from app.models.database import AICollectionRun, AICollectionPriority, AIPriorityFactor, AIRecommendation
from app.services.foms_client import FomsClient

# Stated timezone: Asia/Manila (PHT, UTC+8) for all FOMS Philippine business operations
FOMS_TIMEZONE = timezone(timedelta(hours=8))

def calculate_collection_priorities(
    db: Session,
    ar_data_or_client: Any,
    collection_history_or_trace_id: Any = None,
    trace_id: Optional[str] = None,
    current_date: Optional[date] = None,
    tz: timezone = FOMS_TIMEZONE
) -> Tuple[int, int]:
    """
    On-demand generation of collection priorities.
    Supports either passing FomsClient or passing ar_data list directly.
    Accepts an injectable clock (current_date) and explicit timezone (tz).
    Generates a new AICollectionRun and ranks invoices.
    """
    if isinstance(ar_data_or_client, list):
        ar_data = ar_data_or_client
        collection_history = collection_history_or_trace_id if isinstance(collection_history_or_trace_id, list) else []
        trace_id = trace_id or (collection_history_or_trace_id if isinstance(collection_history_or_trace_id, str) else None)
    else:
        foms_client: FomsClient = ar_data_or_client
        ar_data = foms_client.get_accounts_receivable()
        collection_history = foms_client.get_collection_history()
        trace_id = collection_history_or_trace_id if isinstance(collection_history_or_trace_id, str) else trace_id
    
    # Injectable clock evaluation: default to current date in stated timezone
    if current_date is None:
        current_date = datetime.now(tz).date()

    # Map collection history by invoiceId
    history_map = {}
    for hist in collection_history:
        inv_id = hist.get("invoiceId")
        if inv_id:
            if inv_id not in history_map:
                history_map[inv_id] = []
            history_map[inv_id].append(hist)
            
    # Create the run
    run = AICollectionRun(
        as_of_date=datetime.now(tz),
        model_version="1.0.0",
        status="IN_PROGRESS",
        record_count=len(ar_data),
        trace_id=trace_id
    )
    db.add(run)
    db.flush()

    processed_count = 0
    updated_count = 0

    for ar in ar_data:
        invoice_id = ar["invoiceId"]
        client_id = ar["clientId"]
        outstanding_balance = float(ar.get("outstandingBalance", ar.get("amount", 0)))
        
        # Parse due date
        due_date_str = ar.get("dueDate", current_date.strftime("%Y-%m-%d"))
        try:
            due_date = datetime.strptime(due_date_str, "%Y-%m-%d").date()
        except ValueError:
            due_date = current_date

        # Days overdue strictly computed against injected system clock
        days_overdue = int((current_date - due_date).days)
        
        # Explainable Rule-Based Scoring
        score = 0.0
        priority_level = "LOW"
        explanation = []
        factors = []

        # Factor 1: High Balance (PHP 50,000 threshold)
        is_large_amount = outstanding_balance >= 50000.00
        if is_large_amount:
            score += 40.0
            factors.append({"name": "High Outstanding Balance", "value": f"PHP {outstanding_balance:,.2f}", "contribution": 40.0})
            explanation.append(f"Outstanding balance is high (PHP {outstanding_balance:,.2f}).")
        else:
            factors.append({"name": "Outstanding Balance", "value": f"PHP {outstanding_balance:,.2f}", "contribution": 0.0})

        # Factor 2: Overdue Status
        if days_overdue > 30 and is_large_amount:
            score += 50.0
            factors.append({"name": "Severely Overdue", "value": f"{days_overdue} days", "contribution": 50.0})
            explanation.append(f"Invoice is severely overdue by {days_overdue} days.")
            priority_level = "URGENT"
        elif days_overdue > 30:
            score += 50.0
            factors.append({"name": "Severely Overdue", "value": f"{days_overdue} days", "contribution": 50.0})
            explanation.append(f"Invoice is severely overdue by {days_overdue} days.")
            priority_level = "HIGH"
        elif days_overdue > 0:
            score += 30.0
            factors.append({"name": "Overdue", "value": f"{days_overdue} days", "contribution": 30.0})
            explanation.append(f"Invoice is overdue by {days_overdue} days.")
            priority_level = "HIGH"
        elif 0 <= (due_date - current_date).days <= 7:
            days_left = (due_date - current_date).days
            score += 10.0
            factors.append({"name": "Due Soon", "value": f"{days_left} days left", "contribution": 10.0})
            explanation.append(f"Invoice is due soon ({days_left} days left).")
            priority_level = "MEDIUM" if score >= 40.0 or not is_large_amount else "HIGH"
        else:
            days_left = (due_date - current_date).days
            factors.append({"name": "Not Due", "value": f"{days_left} days left", "contribution": 0.0})
            explanation.append(f"Invoice is not yet due ({days_left} days remaining).")

        # Factor 3: Collection History (Unresolved follow-up)
        inv_history = history_map.get(invoice_id, [])
        unresolved = any(h.get("outcome") == "Unresolved" for h in inv_history)
        if unresolved:
            score += 15.0
            factors.append({"name": "Unresolved History", "value": "Yes", "contribution": 15.0})
            explanation.append("Previous follow-up was unresolved.")
            if priority_level != "URGENT":
                priority_level = "HIGH" if score >= 60.0 else priority_level

        # Clamp score to [0.0, 100.0]
        score = max(0.0, min(100.0, score))

        # Recommendations
        if priority_level == "URGENT":
            rec_text = "Initiate formal collection reminder and escalate to management."
            notice = "Priority: URGENT based on severe overdue status and high balance."
        elif priority_level == "HIGH":
            rec_text = "Initiate formal collection reminder and follow up."
            notice = "Priority: HIGH based on overdue status."
        elif priority_level == "MEDIUM":
            rec_text = "Send official overdue warning email."
            notice = "Priority: MEDIUM. Monitor closely."
        else:
            rec_text = "Monitor invoice aging."
            notice = "Priority: LOW. No immediate action required."

        # Save Priority Result
        priority = AICollectionPriority(
            run_id=run.id,
            invoice_id=invoice_id,
            client_id=client_id,
            score=score,
            priority=priority_level,
            explanation=" ".join(explanation) if explanation else "Invoice is current and in good standing."
        )
        db.add(priority)
        db.flush()
        
        # Save Factors
        for f in factors:
            factor_rec = AIPriorityFactor(
                priority_result_id=priority.id,
                factor_name=f["name"],
                factor_value=str(f["value"]),
                contribution=f["contribution"]
            )
            db.add(factor_rec)
            
        # Save Recommendation
        recommendation = AIRecommendation(
            priority_result_id=priority.id,
            recommendation_text=rec_text,
            decision_support_notice=notice,
            status="Pending Review"
        )
        db.add(recommendation)

        processed_count += 1
        updated_count += 1

    run.status = "COMPLETED"
    db.commit()
    return processed_count, updated_count
