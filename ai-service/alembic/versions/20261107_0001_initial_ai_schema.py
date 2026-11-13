"""Initial schema for FOMS AI Intelligence Service

Revision ID: 20261107_0001
Revises: 
Create Date: 2026-11-07 20:25:00.000000

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

revision: str = '20261107_0001'
down_revision: Union[str, None] = None
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    # 1. ai_duplicate_alerts
    op.create_table(
        'ai_duplicate_alerts',
        sa.Column('id', sa.Integer(), nullable=False),
        sa.Column('alert_type', sa.String(length=50), nullable=False),
        sa.Column('source_record_id', sa.String(length=100), nullable=False),
        sa.Column('matched_record_id', sa.String(length=100), nullable=False),
        sa.Column('confidence_score', sa.Numeric(precision=5, scale=2), nullable=False),
        sa.Column('severity', sa.String(length=20), nullable=True),
        sa.Column('matched_fields', sa.JSON(), nullable=True),
        sa.Column('match_reason', sa.Text(), nullable=False),
        sa.Column('status', sa.String(length=30), nullable=False, server_default='Pending Review'),
        sa.Column('created_at', sa.DateTime(), nullable=True),
        sa.Column('output_version', sa.String(length=20), nullable=True),
        sa.Column('trace_id', sa.String(length=100), nullable=True),
        sa.PrimaryKeyConstraint('id')
    )
    op.create_index(op.f('ix_ai_duplicate_alerts_id'), 'ai_duplicate_alerts', ['id'], unique=False)
    op.create_index(op.f('ix_ai_duplicate_alerts_source_record_id'), 'ai_duplicate_alerts', ['source_record_id'], unique=False)
    op.create_index(op.f('ix_ai_duplicate_alerts_status'), 'ai_duplicate_alerts', ['status'], unique=False)
    op.create_index(op.f('ix_ai_duplicate_alerts_created_at'), 'ai_duplicate_alerts', ['created_at'], unique=False)

    # 2. ai_duplicate_matches
    op.create_table(
        'ai_duplicate_matches',
        sa.Column('id', sa.Integer(), nullable=False),
        sa.Column('alert_id', sa.Integer(), nullable=True),
        sa.Column('source_details', sa.JSON(), nullable=False),
        sa.Column('match_details', sa.JSON(), nullable=False),
        sa.Column('created_at', sa.DateTime(), nullable=True),
        sa.ForeignKeyConstraint(['alert_id'], ['ai_duplicate_alerts.id'], ondelete='CASCADE'),
        sa.PrimaryKeyConstraint('id')
    )
    op.create_index(op.f('ix_ai_duplicate_matches_id'), 'ai_duplicate_matches', ['id'], unique=False)

    # 3. ai_duplicate_reviews
    op.create_table(
        'ai_duplicate_reviews',
        sa.Column('id', sa.Integer(), nullable=False),
        sa.Column('alert_id', sa.Integer(), nullable=True),
        sa.Column('decision', sa.String(length=50), nullable=False),
        sa.Column('justification', sa.Text(), nullable=True),
        sa.Column('reviewed_by', sa.String(length=100), nullable=False),
        sa.Column('reviewed_role', sa.String(length=50), nullable=False),
        sa.Column('reviewed_at', sa.DateTime(), nullable=True),
        sa.Column('trace_id', sa.String(length=100), nullable=True),
        sa.ForeignKeyConstraint(['alert_id'], ['ai_duplicate_alerts.id'], ondelete='CASCADE'),
        sa.PrimaryKeyConstraint('id')
    )
    op.create_index(op.f('ix_ai_duplicate_reviews_id'), 'ai_duplicate_reviews', ['id'], unique=False)

    # 4. ai_collection_runs
    op.create_table(
        'ai_collection_runs',
        sa.Column('id', sa.Integer(), nullable=False),
        sa.Column('as_of_date', sa.DateTime(), nullable=False),
        sa.Column('model_version', sa.String(length=50), nullable=False),
        sa.Column('status', sa.String(length=50), nullable=False),
        sa.Column('record_count', sa.Integer(), nullable=True, server_default='0'),
        sa.Column('generated_at', sa.DateTime(), nullable=True),
        sa.Column('trace_id', sa.String(length=100), nullable=True),
        sa.PrimaryKeyConstraint('id')
    )
    op.create_index(op.f('ix_ai_collection_runs_id'), 'ai_collection_runs', ['id'], unique=False)

    # 5. ai_collection_priorities
    op.create_table(
        'ai_collection_priorities',
        sa.Column('id', sa.Integer(), nullable=False),
        sa.Column('run_id', sa.Integer(), nullable=True),
        sa.Column('invoice_id', sa.String(length=100), nullable=False),
        sa.Column('client_id', sa.String(length=100), nullable=False),
        sa.Column('score', sa.Numeric(precision=5, scale=2), nullable=False),
        sa.Column('priority', sa.String(length=20), nullable=False),
        sa.Column('explanation', sa.Text(), nullable=False),
        sa.Column('created_at', sa.DateTime(), nullable=True),
        sa.ForeignKeyConstraint(['run_id'], ['ai_collection_runs.id'], ondelete='CASCADE'),
        sa.PrimaryKeyConstraint('id')
    )
    op.create_index(op.f('ix_ai_collection_priorities_id'), 'ai_collection_priorities', ['id'], unique=False)
    op.create_index(op.f('ix_ai_collection_priorities_invoice_id'), 'ai_collection_priorities', ['invoice_id'], unique=False)
    op.create_index(op.f('ix_ai_collection_priorities_priority'), 'ai_collection_priorities', ['priority'], unique=False)

    # 6. ai_priority_factors
    op.create_table(
        'ai_priority_factors',
        sa.Column('id', sa.Integer(), nullable=False),
        sa.Column('priority_result_id', sa.Integer(), nullable=True),
        sa.Column('factor_name', sa.String(length=100), nullable=False),
        sa.Column('factor_value', sa.String(length=100), nullable=True),
        sa.Column('contribution', sa.Numeric(precision=5, scale=2), nullable=False),
        sa.ForeignKeyConstraint(['priority_result_id'], ['ai_collection_priorities.id'], ondelete='CASCADE'),
        sa.PrimaryKeyConstraint('id')
    )
    op.create_index(op.f('ix_ai_priority_factors_id'), 'ai_priority_factors', ['id'], unique=False)

    # 7. ai_recommendations
    op.create_table(
        'ai_recommendations',
        sa.Column('id', sa.Integer(), nullable=False),
        sa.Column('priority_result_id', sa.Integer(), nullable=True),
        sa.Column('recommendation_text', sa.String(length=250), nullable=False),
        sa.Column('decision_support_notice', sa.String(length=250), nullable=False),
        sa.Column('status', sa.String(length=30), nullable=True, server_default='Pending Review'),
        sa.Column('created_at', sa.DateTime(), nullable=True),
        sa.ForeignKeyConstraint(['priority_result_id'], ['ai_collection_priorities.id'], ondelete='CASCADE'),
        sa.PrimaryKeyConstraint('id')
    )
    op.create_index(op.f('ix_ai_recommendations_id'), 'ai_recommendations', ['id'], unique=False)
    op.create_index(op.f('ix_ai_recommendations_status'), 'ai_recommendations', ['status'], unique=False)

    # 8. ai_recommendation_decisions
    op.create_table(
        'ai_recommendation_decisions',
        sa.Column('id', sa.Integer(), nullable=False),
        sa.Column('recommendation_id', sa.Integer(), nullable=True),
        sa.Column('decision', sa.String(length=50), nullable=False),
        sa.Column('remarks', sa.Text(), nullable=True),
        sa.Column('decided_by', sa.String(length=100), nullable=False),
        sa.Column('decided_role', sa.String(length=50), nullable=False),
        sa.Column('decided_at', sa.DateTime(), nullable=True),
        sa.Column('trace_id', sa.String(length=100), nullable=True),
        sa.ForeignKeyConstraint(['recommendation_id'], ['ai_recommendations.id'], ondelete='CASCADE'),
        sa.PrimaryKeyConstraint('id')
    )
    op.create_index(op.f('ix_ai_recommendation_decisions_id'), 'ai_recommendation_decisions', ['id'], unique=False)

    # 9. ai_output_logs
    op.create_table(
        'ai_output_logs',
        sa.Column('id', sa.Integer(), nullable=False),
        sa.Column('output_type', sa.String(length=50), nullable=False),
        sa.Column('source_reference', sa.String(length=100), nullable=True),
        sa.Column('version', sa.String(length=20), nullable=True),
        sa.Column('status', sa.String(length=50), nullable=False),
        sa.Column('created_at', sa.DateTime(), nullable=True),
        sa.Column('trace_id', sa.String(length=100), nullable=True),
        sa.PrimaryKeyConstraint('id')
    )
    op.create_index(op.f('ix_ai_output_logs_id'), 'ai_output_logs', ['id'], unique=False)
    op.create_index(op.f('ix_ai_output_logs_source_reference'), 'ai_output_logs', ['source_reference'], unique=False)

    # 10. ai_audit_events
    op.create_table(
        'ai_audit_events',
        sa.Column('event_id', sa.String(length=100), nullable=False),
        sa.Column('occurred_at', sa.DateTime(), nullable=True),
        sa.Column('user_id', sa.String(length=100), nullable=True),
        sa.Column('full_name', sa.String(length=150), nullable=True),
        sa.Column('role_name', sa.String(length=50), nullable=True),
        sa.Column('event_type', sa.String(length=100), nullable=False),
        sa.Column('action_description', sa.Text(), nullable=False),
        sa.Column('related_record_type', sa.String(length=50), nullable=True),
        sa.Column('source_reference', sa.String(length=100), nullable=True),
        sa.Column('normalized_reference', sa.String(length=100), nullable=True),
        sa.Column('result', sa.String(length=50), nullable=True, server_default='SUCCESS'),
        sa.Column('ip_address', sa.String(length=50), nullable=True),
        sa.Column('user_agent', sa.Text(), nullable=True),
        sa.Column('details', sa.JSON(), nullable=True),
        sa.Column('correlation_id', sa.String(length=100), nullable=True),
        sa.PrimaryKeyConstraint('event_id')
    )
    op.create_index(op.f('ix_ai_audit_events_event_id'), 'ai_audit_events', ['event_id'], unique=False)
    op.create_index(op.f('ix_ai_audit_events_event_type'), 'ai_audit_events', ['event_type'], unique=False)
    op.create_index(op.f('ix_ai_audit_events_occurred_at'), 'ai_audit_events', ['occurred_at'], unique=False)

    # 11. ai_unique_documents
    op.create_table(
        'ai_unique_documents',
        sa.Column('id', sa.Integer(), nullable=False),
        sa.Column('document_type', sa.String(length=50), nullable=False),
        sa.Column('document_number', sa.String(length=100), nullable=False),
        sa.Column('client_name', sa.String(length=200), nullable=True),
        sa.Column('amount', sa.Numeric(precision=12, scale=2), nullable=True),
        sa.Column('transaction_date', sa.String(length=50), nullable=True),
        sa.Column('reference_number', sa.String(length=100), nullable=True),
        sa.Column('waybill_number', sa.String(length=100), nullable=True),
        sa.Column('source_type', sa.String(length=20), nullable=True, server_default='Uploaded'),
        sa.Column('scanned_by', sa.String(length=100), nullable=True),
        sa.Column('scanned_role', sa.String(length=50), nullable=True),
        sa.Column('ai_result', sa.String(length=50), nullable=True, server_default='No Duplicate Detected'),
        sa.Column('similarity_score', sa.Numeric(precision=5, scale=2), nullable=True, server_default='0.0'),
        sa.Column('created_at', sa.DateTime(), nullable=True),
        sa.Column('trace_id', sa.String(length=100), nullable=True),
        sa.PrimaryKeyConstraint('id')
    )
    op.create_index(op.f('ix_ai_unique_documents_id'), 'ai_unique_documents', ['id'], unique=False)
    op.create_index(op.f('ix_ai_unique_documents_document_number'), 'ai_unique_documents', ['document_number'], unique=False)
    op.create_index(op.f('ix_ai_unique_documents_created_at'), 'ai_unique_documents', ['created_at'], unique=False)

    # 12. ai_review_history
    op.create_table(
        'ai_review_history',
        sa.Column('id', sa.Integer(), nullable=False),
        sa.Column('target_type', sa.String(length=50), nullable=False),
        sa.Column('target_id', sa.String(length=100), nullable=False),
        sa.Column('reviewer_username', sa.String(length=100), nullable=False),
        sa.Column('reviewer_role', sa.String(length=50), nullable=False),
        sa.Column('decision', sa.String(length=50), nullable=False),
        sa.Column('remarks', sa.Text(), nullable=True),
        sa.Column('recommended_action', sa.String(length=150), nullable=True),
        sa.Column('review_date', sa.DateTime(), nullable=True),
        sa.Column('trace_id', sa.String(length=100), nullable=True),
        sa.PrimaryKeyConstraint('id')
    )
    op.create_index(op.f('ix_ai_review_history_id'), 'ai_review_history', ['id'], unique=False)
    op.create_index(op.f('ix_ai_review_history_review_date'), 'ai_review_history', ['review_date'], unique=False)

    # 13. ai_scan_logs
    op.create_table(
        'ai_scan_logs',
        sa.Column('id', sa.Integer(), nullable=False),
        sa.Column('user_id', sa.String(length=100), nullable=True),
        sa.Column('uploaded_file_name', sa.String(length=255), nullable=False),
        sa.Column('detected_document_type', sa.String(length=100), nullable=True),
        sa.Column('is_allowed', sa.Boolean(), nullable=True, server_default='false'),
        sa.Column('validation_status', sa.String(length=50), nullable=True),
        sa.Column('reason', sa.Text(), nullable=True),
        sa.Column('created_at', sa.DateTime(), nullable=True),
        sa.PrimaryKeyConstraint('id')
    )
    op.create_index(op.f('ix_ai_scan_logs_id'), 'ai_scan_logs', ['id'], unique=False)
    op.create_index(op.f('ix_ai_scan_logs_created_at'), 'ai_scan_logs', ['created_at'], unique=False)


def downgrade() -> None:
    op.drop_table('ai_scan_logs')
    op.drop_table('ai_review_history')
    op.drop_table('ai_unique_documents')
    op.drop_table('ai_audit_events')
    op.drop_table('ai_output_logs')
    op.drop_table('ai_recommendation_decisions')
    op.drop_table('ai_recommendations')
    op.drop_table('ai_priority_factors')
    op.drop_table('ai_collection_priorities')
    op.drop_table('ai_collection_runs')
    op.drop_table('ai_duplicate_reviews')
    op.drop_table('ai_duplicate_matches')
    op.drop_table('ai_duplicate_alerts')
