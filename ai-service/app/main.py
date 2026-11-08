import hmac
import os
import uuid
import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI, Request, status
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from slowapi import Limiter, _rate_limit_exceeded_handler
from slowapi.util import get_remote_address
from slowapi.errors import RateLimitExceeded

from app.core.config import settings
from app.models.database import Base, engine
from app.api.routes import (
    duplicate_routes, collection_routes,
    recommendation_routes, health_routes, dashboard_routes, auth_routes,
    payment_ai_routes
)

logging.basicConfig(level=logging.INFO, handlers=[logging.FileHandler("error.log"), logging.StreamHandler()])
logger = logging.getLogger(__name__)

from app.core.rate_limit import limiter


@asynccontextmanager
async def lifespan(app: FastAPI):
    logger.info("Initializing AI Service...")
    
    # Security check: verify AI_SERVICE_API_KEY is configured
    api_key = settings.AI_SERVICE_API_KEY.strip() if settings.AI_SERVICE_API_KEY else ""
    forbidden_tokens = ["change-me", "change_me", "replace_with", "your_", "example", "placeholder"]
    if not api_key or len(api_key) < 16 or any(tok in api_key.lower() for tok in forbidden_tokens):
        if os.getenv("FOMS_ENV") != "testing" and os.getenv("PYTEST_CURRENT_TEST") is None:
            raise RuntimeError(
                "Fatal Startup Error: AI_SERVICE_API_KEY is missing, too short (<16 chars), "
                "or set to a placeholder/default value. Set a secure service key in environment configuration."
            )

    # Database connectivity check (DDL migrations are managed externally via Alembic, not create_all at runtime)
    try:
        from sqlalchemy import text
        with engine.connect() as conn:
            conn.execute(text("SELECT 1"))
        logger.info("Database connectivity check passed.")
    except Exception as e:
        logger.warning(f"Database connectivity check warning (tables managed via Alembic): {type(e).__name__}")

    yield
    logger.info("Shutting down AI Service...")


app = FastAPI(
    title=settings.PROJECT_NAME,
    version="1.0.0",
    lifespan=lifespan,
)

# Attach rate limiter to app state
app.state.limiter = limiter
app.add_exception_handler(RateLimitExceeded, _rate_limit_exceeded_handler)


# ── §22 CORS Restriction ────────────────────────────────────────────────
# Only allow known frontend origins — never use "*" in production
ALLOWED_ORIGINS = os.getenv(
    "CORS_ALLOWED_ORIGINS",
    "http://localhost:5174,http://localhost:5175,http://localhost:5176,http://localhost:80,http://localhost,http://127.0.0.1:5174,http://127.0.0.1:5175,http://127.0.0.1:5176"
).split(",")

app.add_middleware(
    CORSMiddleware,
    allow_origins=ALLOWED_ORIGINS,
    allow_credentials=True,
    allow_methods=["GET", "POST", "PUT", "DELETE", "OPTIONS"],
    allow_headers=["Authorization", "Content-Type", "X-API-Key", "X-User-Role",
                    "X-User-Username", "X-Client-Id", "X-Account-Type",
                    "X-Password-Version", "X-Service-Token"],
)


# ── §20 Secure Error Handling ────────────────────────────────────────────
@app.exception_handler(Exception)
async def global_exception_handler(request: Request, exc: Exception):
    """
    Global exception handler — §20 Secure Error Handling.
    Never expose stack traces, SQL errors, internal paths, connection strings,
    JWT secrets, API keys, or Gemini credentials to the client.
    """
    trace_id = str(uuid.uuid4())
    logger.error(f"[{trace_id}] Unhandled exception on {request.url.path}: {exc}", exc_info=True)
    return JSONResponse(
        status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
        content={
            "success": False,
            "message": "An internal error occurred. Please contact support if this persists.",
            "traceId": trace_id,
        },
    )


# ── API Secret Check Middleware (service-to-service) ─────────────────────
@app.middleware("http")
async def verify_gateway_api_key(request: Request, call_next):
    # Skip docs and health checks from key requirements
    if request.url.path in ["/health", "/health/ready", "/docs", "/openapi.json", "/redoc"]:
        return await call_next(request)

    api_key = request.headers.get("X-API-Key")
    if not api_key:
        auth_header = request.headers.get("Authorization")
        if auth_header and auth_header.startswith("ApiKey "):
            api_key = auth_header.split(" ")[1]

    expected_key = settings.AI_SERVICE_API_KEY.strip() if settings.AI_SERVICE_API_KEY else ""
    if not expected_key or not api_key or not hmac.compare_digest(api_key.strip(), expected_key):
        return JSONResponse(
            status_code=status.HTTP_403_FORBIDDEN,
            content={
                "success": False,
                "message": "Access Forbidden: Invalid Service API Key",
                "traceId": str(uuid.uuid4()),
            },
        )

    response = await call_next(request)
    return response


# ── §22 Security Headers Middleware ──────────────────────────────────────
@app.middleware("http")
async def add_security_headers(request: Request, call_next):
    response = await call_next(request)
    response.headers["X-Content-Type-Options"] = "nosniff"
    response.headers["X-Frame-Options"] = "DENY"
    response.headers["X-XSS-Protection"] = "1; mode=block"
    response.headers["Referrer-Policy"] = "strict-origin-when-cross-origin"
    response.headers["Cache-Control"] = "no-store"
    response.headers["Pragma"] = "no-cache"
    return response


# ── Include main routers ────────────────────────────────────────────────
app.include_router(auth_routes.router, prefix="/api/ai/auth", tags=["auth"])
app.include_router(dashboard_routes.router, prefix="/api/ai/dashboard", tags=["dashboard"])
app.include_router(dashboard_routes.router, prefix="/api/ai", tags=["global_ai"])
app.include_router(duplicate_routes.router, prefix="/api/ai/duplicates", tags=["duplicates"])
app.include_router(duplicate_routes.router, prefix="/api/ai/documents", tags=["documents"])
app.include_router(collection_routes.router, prefix="/api/ai/collection", tags=["collection"])
app.include_router(recommendation_routes.router, prefix="/api/ai/recommendations", tags=["recommendations"])
app.include_router(payment_ai_routes.router, prefix="/api/ai", tags=["payments"])
app.include_router(health_routes.router, prefix="/health", tags=["health"])
