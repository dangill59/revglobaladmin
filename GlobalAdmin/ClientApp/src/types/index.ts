export interface User {
  email: string;
}

export interface Workspace {
  id: string;
  name: string;
  ownerUser: string;
  maxUsers: number;
  isDisabled: boolean;
  created: string | null;
  userCount: number;
  documentCount: number;
  databaseSizeBytes: number;
}

export interface WorkspaceDetails extends Workspace {
  stats: {
    userCount: number;
    documentCount: number;
    databaseSizeBytes: number;
    storageSizeBytes: number;
    licenseCount: number;
  };
  settings: {
    softDeleteEnabled: boolean;
    softDeleteRetentionDays: number;
    softDeleteAutoDelete: boolean;
    softDeleteRequireReason: boolean;
    softDeleteNotifyOnPurge: boolean;
    featureFullTextOcr: boolean;
    featureBarcode: boolean;
    featureScripts: boolean;
    featureTwoFactor: boolean;
    ocrEngine: string;
    googleOcrQuota: number;
    inactivityTimeout: number;
    activityRetentionHours: number;
    maxImmediateSizeMB: number;
    suspendProcessing: boolean;
    customBrandingEnabled: boolean;
    brandingLogoUrl: string;
    brandingPrimaryColor: string;
    auditLogsEnabled: boolean;
  };
}

export interface WorkspaceUser {
  id: string;
  userName: string;
  emailAddress: string;
  preferredName: string;
  isAdmin: boolean;
}

export interface GlobalStats {
  totalWorkspaces: number;
  totalUsers: number;
  totalDocuments: number;
  totalDatabaseSizeBytes: number;
  totalStorageSizeBytes: number;
  topWorkspacesBySize: WorkspaceStatSummary[];
  topWorkspacesByDocuments: WorkspaceStatSummary[];
}

export interface WorkspaceStatSummary {
  workspaceId: string;
  workspaceName: string;
  databaseSizeBytes: number;
  documentCount: number;
  userCount: number;
}

export interface Install {
  id: string;
  customerName: string;
  contactEmail: string;
  version: string;
  status: string;
  registeredAt: string;
  lastHeartbeat: string;
  metrics: {
    activeUsers: number;
    totalDocuments: number;
    storageUsedBytes: number;
  } | null;
  license: {
    maxUsers: number;
    tier: string;
  } | null;
}

export interface InstallSummary {
  total: number;
  healthy: number;
  warning: number;
  offline: number;
  totalDocuments: number;
  totalStorage: number;
}

export interface RegistrationKey {
  key: string;
  customerName: string;
  contactEmail: string;
  maxUsers: number;
  tier: string;
  expiresAt: string;
  createdAt: string;
}

export interface WorkspaceSettings {
  maxUsers?: number;
  // Soft Delete
  softDeleteEnabled?: boolean;
  softDeleteRetentionDays?: number;
  softDeleteAutoDelete?: boolean;
  softDeleteRequireReason?: boolean;
  softDeleteNotifyOnPurge?: boolean;
  // Features
  featureFullTextOcr?: boolean;
  featureBarcode?: boolean;
  featureScripts?: boolean;
  featureTwoFactor?: boolean;
  // OCR
  ocrEngine?: string;
  googleOcrQuota?: number;
  // Processing
  inactivityTimeout?: number;
  activityRetentionHours?: number;
  maxImmediateSizeMB?: number;
  suspendProcessing?: boolean;
  // Custom Branding
  customBrandingEnabled?: boolean;
  brandingLogoUrl?: string;
  brandingPrimaryColor?: string;
  // Audit Logs
  auditLogsEnabled?: boolean;
}
