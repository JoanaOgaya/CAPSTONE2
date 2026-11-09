import asyncio

import pytest
from fastapi import HTTPException
from starlette.requests import Request

from app.auth import policies
from app.constants.roles import DUPLICATE_CHECK_ROLES, Roles


def _request(headers: list[tuple[bytes, bytes]]) -> Request:
    return Request({
        "type": "http",
        "method": "GET",
        "path": "/api/ai/duplicates",
        "headers": headers,
    })


def test_forwarded_financial_manager_role_is_canonicalized():
    request = _request([
        (b"x-user-role", b"Finance Manager"),
        (b"x-user-username", b"fm-user"),
    ])

    assert policies.get_current_user(request)["role"] == Roles.FINANCE_MANAGER.value


def test_direct_financial_manager_role_is_canonicalized(monkeypatch):
    monkeypatch.setattr(
        policies,
        "decode_and_validate",
        lambda token: {"sub": "fm-user", "role": "Finance Manager"},
    )
    request = _request([(b"authorization", b"Bearer valid-token")])

    assert policies.get_current_user(request)["role"] == Roles.FINANCE_MANAGER.value


def test_duplicate_alert_list_permission_accepts_canonical_financial_manager():
    role_checker = policies.require_roles(*DUPLICATE_CHECK_ROLES)

    payload = asyncio.run(role_checker(payload={"role": Roles.FINANCE_MANAGER.value}))

    assert payload["role"] == Roles.FINANCE_MANAGER.value


def test_unknown_forwarded_role_is_forbidden():
    request = _request([
        (b"x-user-role", b"Unknown Manager"),
        (b"x-user-username", b"user"),
    ])

    with pytest.raises(HTTPException) as exc:
        policies.get_current_user(request)

    assert exc.value.status_code == 403


def test_client_forwarded_role_is_forbidden():
    request = _request([
        (b"x-user-role", b"Client"),
        (b"x-user-username", b"client-user"),
    ])

    with pytest.raises(HTTPException) as exc:
        policies.get_current_user(request)

    assert exc.value.status_code == 403


def test_client_account_type_is_forbidden_even_with_staff_role():
    with pytest.raises(HTTPException) as exc:
        policies._canonicalize_identity({
            "role": Roles.ACCOUNTANT.value,
            "account_type": "Client",
        })

    assert exc.value.status_code == 403


def test_recommendation_decision_role_enforcement():
    """Server-side enforcement: Only Finance Manager and Head Accountant can decide recommendations."""
    from app.constants.roles import RECOMMENDATION_DECIDE_ROLES
    checker = policies.require_roles(*RECOMMENDATION_DECIDE_ROLES)

    # Allowed roles
    assert asyncio.run(checker(payload={"role": Roles.FINANCE_MANAGER.value}))["role"] == Roles.FINANCE_MANAGER.value
    assert asyncio.run(checker(payload={"role": Roles.HEAD_ACCOUNTANT.value}))["role"] == Roles.HEAD_ACCOUNTANT.value

    # Forbidden roles must be rejected with 403 server-side
    for forbidden_role in [Roles.ACCOUNTANT.value, Roles.COORDINATOR.value, Roles.ASSISTANT_OF_FINANCE_MANAGER.value]:
        with pytest.raises(HTTPException) as exc:
            asyncio.run(checker(payload={"role": forbidden_role}))
        assert exc.value.status_code == 403


@pytest.mark.anyio
async def test_service_api_key_middleware_missing_key(monkeypatch):
    """Requests to protected AI routes without X-API-Key must return 403 Forbidden."""
    import httpx
    from app.main import app
    from app.core.config import settings

    monkeypatch.setattr(settings, "AI_SERVICE_API_KEY", "valid-test-key-32characters-long")
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://testserver") as client:
        response = await client.get("/api/ai/recommendations")
        assert response.status_code == 403
        assert "Invalid Service API Key" in response.json().get("message", "")


@pytest.mark.anyio
async def test_service_api_key_middleware_wrong_key(monkeypatch):
    """Requests with an invalid X-API-Key must return 403 Forbidden."""
    import httpx
    from app.main import app
    from app.core.config import settings

    monkeypatch.setattr(settings, "AI_SERVICE_API_KEY", "valid-test-key-32characters-long")
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://testserver") as client:
        response = await client.get("/api/ai/recommendations", headers={"X-API-Key": "completely-invalid-key-xyz"})
        assert response.status_code == 403
        assert "Invalid Service API Key" in response.json().get("message", "")


@pytest.mark.anyio
async def test_service_api_key_middleware_correct_key(monkeypatch):
    """Requests with the configured AI_SERVICE_API_KEY pass the service auth middleware."""
    import httpx
    from app.main import app
    from app.core.config import settings

    test_key = "valid-test-key-32characters-long"
    monkeypatch.setattr(settings, "AI_SERVICE_API_KEY", test_key)
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://testserver") as client:
        # Passing valid service key allows request past middleware to route auth (returns 401 missing user JWT, not 403 service key error)
        response = await client.get("/api/ai/recommendations", headers={"X-API-Key": test_key})
        assert response.status_code == 401
        assert "Invalid Service API Key" not in response.text


def test_phase5_ocr_and_duplicate_scan_403_for_fm_and_ha():
    """Phase 5: Finance Manager and Head Accountant must receive 403 on OCR and duplicate scan routes."""
    from app.constants.roles import DOCUMENT_SCAN_ROLES

    doc_checker = policies.require_roles(*DOCUMENT_SCAN_ROLES)

    for allowed_role in [Roles.ACCOUNTANT.value, Roles.ASSISTANT_OF_FINANCE_MANAGER.value]:
        assert asyncio.run(doc_checker(payload={"role": allowed_role}))["role"] == allowed_role

    for forbidden_role in [Roles.FINANCE_MANAGER.value, Roles.HEAD_ACCOUNTANT.value]:
        with pytest.raises(HTTPException) as exc1:
            asyncio.run(doc_checker(payload={"role": forbidden_role}))
        assert exc1.value.status_code == 403


def test_phase5_duplicate_review_403_for_accountant_and_afm():
    """Phase 5: Only Finance Manager and Head Accountant may review/approve duplicate alerts; Accountant & AFM get 403."""
    from app.constants.roles import DUPLICATE_REVIEW_ROLES

    review_checker = policies.require_roles(*DUPLICATE_REVIEW_ROLES)

    for allowed_role in [Roles.FINANCE_MANAGER.value, Roles.HEAD_ACCOUNTANT.value]:
        assert asyncio.run(review_checker(payload={"role": allowed_role}))["role"] == allowed_role

    for forbidden_role in [Roles.ACCOUNTANT.value, Roles.ASSISTANT_OF_FINANCE_MANAGER.value]:
        with pytest.raises(HTTPException) as exc:
            asyncio.run(review_checker(payload={"role": forbidden_role}))
        assert exc.value.status_code == 403


def test_phase5_duplicate_review_separation_of_duties_self_approval_403():
    """Phase 5: Submitter != Approver enforced on duplicate alert review in ai-service."""
    from unittest.mock import MagicMock
    from app.api.routes.duplicate_routes import review_duplicate_alert
    from app.schemas.schemas import DuplicateReviewRequest

    mock_db = MagicMock()
    mock_alert = MagicMock()
    mock_alert.id = 101
    mock_alert.matched_fields = {"submittedBy": "EMP-002", "decidedBy": "EMP-002"}
    mock_alert.matches = []
    mock_alert.status = "Pending Review"
    mock_db.query.return_value.filter.return_value.first.return_value = mock_alert

    req = _request([(b"x-user-role", b"Head Accountant"), (b"x-user-username", b"EMP-002")])
    body = DuplicateReviewRequest(decision="APPROVE", justification="Self approving")
    with pytest.raises(HTTPException) as exc:
        review_duplicate_alert(
            alertId=101,
            request=req,
            body=body,
            db=mock_db,
            payload={"sub": "EMP-002", "name": "EMP-002", "role": Roles.HEAD_ACCOUNTANT.value},
        )
    assert exc.value.status_code == 403
    assert "Separation of duties" in str(exc.value.detail)
