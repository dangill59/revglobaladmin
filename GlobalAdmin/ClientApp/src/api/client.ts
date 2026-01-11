import axios from 'axios';
import type {
  Workspace, WorkspaceDetails, WorkspaceUser, WorkspaceSettings,
  GlobalStats, Install, InstallSummary, RegistrationKey
} from '../types';

const api = axios.create({
  baseURL: '/api',
  withCredentials: true
});

// Add response interceptor to handle 401 (unauthorized)
api.interceptors.response.use(
  response => response,
  error => {
    if (error.response?.status === 401) {
      // Redirect to login if not already there
      if (window.location.pathname !== '/login') {
        window.location.href = '/login';
      }
    }
    return Promise.reject(error);
  }
);

// Auth
export const authApi = {
  login: (email: string, password: string) =>
    api.post<{ email: string }>('/auth/login', { email, password }),
  logout: () => api.post('/auth/logout'),
  me: () => api.get<{ email: string }>('/auth/me')
};

// Workspaces
export const workspacesApi = {
  getAll: () => api.get<Workspace[]>('/workspaces'),
  getById: (id: string) => api.get<WorkspaceDetails>(`/workspaces/${id}`),
  create: (name: string, ownerEmail: string, maxUsers: number) =>
    api.post<{ id: string; name: string }>('/workspaces', { name, ownerEmail, maxUsers }),
  updateSettings: (id: string, settings: WorkspaceSettings) =>
    api.put(`/workspaces/${id}/settings`, settings),
  updateLicense: (id: string, maxUsers: number) =>
    api.put(`/workspaces/${id}/license`, { maxUsers }),
  disable: (id: string) => api.post(`/workspaces/${id}/disable`),
  enable: (id: string) => api.post(`/workspaces/${id}/enable`),
  delete: (id: string) => api.delete(`/workspaces/${id}`),
  getUsers: (id: string) => api.get<WorkspaceUser[]>(`/workspaces/${id}/users`),
  triggerReindex: (id: string) => api.post<{ message: string }>(`/workspaces/${id}/reindex`)
};

// Analytics
export const analyticsApi = {
  getGlobalStats: () => api.get<GlobalStats>('/analytics/global')
};

// Admin Users
export const usersApi = {
  getAll: () => api.get<{ email: string }[]>('/admin/users'),
  add: (email: string) => api.post('/admin/users', { email }),
  remove: (email: string) => api.delete(`/admin/users/${encodeURIComponent(email)}`)
};

// On-Prem Installs
export const installsApi = {
  getAll: () => api.get<Install[]>('/installs'),
  getById: (id: string) => api.get<Install>(`/installs/${id}`),
  getSummary: () => api.get<InstallSummary>('/installs/summary')
};

// Registration Keys
export const keysApi = {
  getAll: () => api.get<RegistrationKey[]>('/registrationkeys'),
  generate: (data: {
    customerName: string;
    contactEmail: string;
    maxUsers?: number;
    tier?: string;
    validityDays?: number
  }) => api.post<RegistrationKey>('/registrationkeys', data),
  revoke: (key: string) => api.delete(`/registrationkeys/${key}`)
};

export default api;
