/**
 * App — providers + routes.
 * OneUI Design System v2.0 Architecture.
 * Mounts standard FOMS modules under /foms/* and provides dedicated print views outside the layout shell.
 */
import React from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { ToastProvider } from './components/ToastContext';
import ToastBar from './components/ToastBar';
import { AuthProvider, useAuth } from './context/AuthContext';
import { ProtectedRoute, RoleGuard } from './components/ProtectedRoute';
import MainLayout from './components/MainLayout';
import { ErrorBoundary } from './components/ErrorBoundary';
import { useCancelButtonFeedback } from './hooks/useCancelButtonFeedback';
import { LANDING_PATH, MODULES, type ModuleKey } from './utils/roleAccess';

import { LoginPage } from './pages/LoginPage';
import SpeedPay from './pages/SpeedPay';
import Dashboard from './pages/Dashboard';
import ProfilePage from './pages/ProfilePage';
import NotificationsPage from './pages/NotificationsPage';
import ClientManagement from './pages/ClientManagement';
import RateConfiguration from './pages/RateConfiguration';
import Waybills from './pages/Waybills';
import BillingValidation from './pages/BillingValidation';
import InvoiceCreation from './pages/InvoiceCreation';
import InvoicingDesk from './pages/InvoicingDesk';
import InvoiceReview from './pages/InvoiceReview';
import AccountsReceivable from './pages/AccountsReceivable';
import Payments from './pages/Payments';
import Receipts from './pages/Receipts';
import Settlements from './pages/Settlements';
import RecordSettlement from './pages/RecordSettlement';
import SpeedPayValidation from './pages/SpeedPayValidation';
import FinancialAdjustments from './pages/FinancialAdjustments';
import LiquidationValidation from './pages/LiquidationValidation';
import CashFlowManagement from './pages/CashFlowManagement';
import AIDuplicateScan from './pages/AIDuplicateScan';
import MySubmissions from './pages/MySubmissions';
import { ManualReviewQueue } from './pages/ManualReviewQueue';
import UniqueDocuments from './pages/UniqueDocuments';
import FlaggedDuplicates from './pages/FlaggedDuplicates';
import ReviewHistory from './pages/ReviewHistory';
import ForReview from './pages/ForReview';
import Reports from './pages/Reports';
import { AuditLogs } from './pages/AuditLogs';
import DocumentExtraction from './pages/DocumentExtraction';
import AIOutputHistory from './pages/AIOutputHistory';
import StarsCollectionWorkflow from './pages/StarsCollectionWorkflow';
import DmsBillingIntake from './pages/DmsBillingIntake';
import CashAdvanceLiquidationPortal from './pages/CashAdvanceLiquidationPortal';
import ApprovalsQueue from './pages/ApprovalsQueue';
import MyCashAdvances from './pages/MyCashAdvances';
import InvoicePrintView from './pages/InvoicePrintView';
import { ErrorPage } from './components/shared/errors/ErrorPage';

const EscalatedApprovalsPage: React.FC = () => <ApprovalsQueue mode="escalated" />;

const PAGES: Record<ModuleKey, React.ComponentType> = {
  dashboard: Dashboard,
  approvals: ApprovalsQueue,
  escalatedApprovals: EscalatedApprovalsPage,
  myCashAdvances: MyCashAdvances,
  clients: ClientManagement,
  rates: RateConfiguration,
  waybills: Waybills,
  billingValidation: BillingValidation,
  invoiceCreate: InvoiceCreation,
  invoicingDesk: InvoicingDesk,
  invoiceReview: InvoiceReview,
  accountsReceivable: AccountsReceivable,
  starsCollection: StarsCollectionWorkflow,
  dmsIntake: DmsBillingIntake,
  cashAdvancePortal: CashAdvanceLiquidationPortal,
  payments: Payments,
  receipts: Receipts,
  settlements: Settlements,
  recordSettlement: RecordSettlement,
  speedpayValidation: SpeedPayValidation,
  adjustments: FinancialAdjustments,
  liquidations: LiquidationValidation,
  cashFlow: CashFlowManagement,
  duplicateScan: AIDuplicateScan,
  mySubmissions: MySubmissions,
  manualReviewQueue: ManualReviewQueue,
  uniqueDocuments: UniqueDocuments,
  flaggedDuplicates: FlaggedDuplicates,
  reviewHistory: ReviewHistory,
  documentExtraction: DocumentExtraction,
  forReview: ForReview,
  reports: Reports,
  auditLogs: AuditLogs,
  aiHistory: AIOutputHistory,
  profile: ProfilePage,
  notifications: NotificationsPage,
};

/** "/" and unknown URLs send each role to its own landing page. */
const RoleHome: React.FC = () => {
  const { user } = useAuth();
  return <Navigate to={user ? LANDING_PATH[user.role] : '/login'} replace />;
};

const AppRoutes: React.FC = () => {
  useCancelButtonFeedback();
  return (
    <Routes>
      {/* Public Routes */}
      <Route path="/login" element={<LoginPage />} />
      <Route path="/speedpay" element={<SpeedPay />} />

      {/* BIL-04: Standalone Invoice Print View (Outside Layout Shell) */}
      <Route
        path="/foms/print/invoice/:id"
        element={
          <ProtectedRoute>
            <InvoicePrintView />
          </ProtectedRoute>
        }
      />

      {/* Main OneUI Layout Shell */}
      <Route
        element={
          <ProtectedRoute>
            <MainLayout />
          </ProtectedRoute>
        }
      >
        {/* /foms/* Canonical Hierarchy Routes */}
        <Route path="/foms/dashboard" element={<Dashboard />} />
        <Route path="/foms/approvals" element={<ApprovalsQueue />} />
        <Route path="/foms/clients" element={<ClientManagement />} />
        <Route path="/foms/clients/:id" element={<ClientManagement />} />
        <Route path="/foms/waybills" element={<Waybills />} />
        <Route path="/foms/waybills/:id" element={<Waybills />} />
        <Route path="/foms/invoices" element={<InvoicingDesk />} />
        <Route path="/foms/invoices/create" element={<InvoiceCreation />} />
        <Route path="/foms/invoices/:id" element={<InvoicingDesk />} />
        <Route path="/foms/accounts-receivable" element={<AccountsReceivable />} />
        <Route path="/foms/payments" element={<Payments />} />
        <Route path="/foms/settlements" element={<Settlements />} />
        <Route path="/foms/adjustments" element={<FinancialAdjustments />} />
        <Route path="/foms/liquidation" element={<LiquidationValidation />} />
        <Route path="/foms/cash-flow" element={<CashFlowManagement />} />
        <Route path="/foms/budget" element={<Reports />} />
        <Route path="/foms/reports" element={<Reports />} />
        <Route path="/foms/ai-insights" element={<AIDuplicateScan />} />
        <Route path="/foms/audit-log" element={<AuditLogs />} />
        <Route path="/profile" element={<ProfilePage />} />
        <Route path="/notifications" element={<NotificationsPage />} />

        {/* Dynamic Legacy Module Routes */}
        {MODULES.flatMap((m) => {
          const Page = PAGES[m.key];
          const element = (
            <RoleGuard module={m.key}>
              <ErrorBoundary fallbackTitle={`Error loading ${m.label}`}>
                <Page />
              </ErrorBoundary>
            </RoleGuard>
          );
          return [m.path, ...(m.detailPath ? [m.detailPath] : [])].map((path) => (
            <Route key={path} path={path} element={element} />
          ));
        })}

        <Route path="/ai/output-history" element={<Navigate to="/ai-history" replace />} />
        <Route path="/" element={<RoleHome />} />
        <Route path="*" element={<ErrorPage code={404} />} />
      </Route>
    </Routes>
  );
};

const App: React.FC = () => (
  <BrowserRouter>
    <AuthProvider>
      <ToastProvider>
        <ToastBar />
        <AppRoutes />
      </ToastProvider>
    </AuthProvider>
  </BrowserRouter>
);

export default App;
