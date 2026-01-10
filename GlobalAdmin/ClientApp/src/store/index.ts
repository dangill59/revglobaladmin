import { configureStore, createSlice, createAsyncThunk } from '@reduxjs/toolkit';
import { authApi, workspacesApi, analyticsApi, usersApi, installsApi, keysApi } from '../api/client';
import type {
  Workspace, WorkspaceDetails, GlobalStats,
  Install, InstallSummary, RegistrationKey
} from '../types';

// Auth slice
interface AuthState {
  user: { email: string } | null;
  loading: boolean;
  error: string | null;
}

const initialAuthState: AuthState = {
  user: null,
  loading: true,
  error: null
};

export const checkAuth = createAsyncThunk('auth/check', async () => {
  const response = await authApi.me();
  return response.data;
});

export const login = createAsyncThunk('auth/login',
  async ({ email, password }: { email: string; password: string }) => {
    const response = await authApi.login(email, password);
    return response.data;
  }
);

export const logout = createAsyncThunk('auth/logout', async () => {
  await authApi.logout();
});

const authSlice = createSlice({
  name: 'auth',
  initialState: initialAuthState,
  reducers: {
    clearError: (state) => { state.error = null; }
  },
  extraReducers: (builder) => {
    builder
      .addCase(checkAuth.pending, (state) => { state.loading = true; })
      .addCase(checkAuth.fulfilled, (state, action) => {
        state.loading = false;
        state.user = action.payload;
      })
      .addCase(checkAuth.rejected, (state) => {
        state.loading = false;
        state.user = null;
      })
      .addCase(login.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(login.fulfilled, (state, action) => {
        state.loading = false;
        state.user = action.payload;
      })
      .addCase(login.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message || 'Login failed';
      })
      .addCase(logout.fulfilled, (state) => {
        state.user = null;
      });
  }
});

// Workspaces slice
interface WorkspacesState {
  list: Workspace[];
  current: WorkspaceDetails | null;
  loading: boolean;
  error: string | null;
}

const initialWorkspacesState: WorkspacesState = {
  list: [],
  current: null,
  loading: false,
  error: null
};

export const fetchWorkspaces = createAsyncThunk('workspaces/fetchAll', async () => {
  const response = await workspacesApi.getAll();
  return response.data;
});

export const fetchWorkspace = createAsyncThunk('workspaces/fetchOne',
  async (id: string) => {
    const response = await workspacesApi.getById(id);
    return response.data;
  }
);

const workspacesSlice = createSlice({
  name: 'workspaces',
  initialState: initialWorkspacesState,
  reducers: {
    clearCurrent: (state) => { state.current = null; }
  },
  extraReducers: (builder) => {
    builder
      .addCase(fetchWorkspaces.pending, (state) => { state.loading = true; })
      .addCase(fetchWorkspaces.fulfilled, (state, action) => {
        state.loading = false;
        state.list = action.payload;
      })
      .addCase(fetchWorkspaces.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message || 'Failed to fetch workspaces';
      })
      .addCase(fetchWorkspace.pending, (state) => { state.loading = true; })
      .addCase(fetchWorkspace.fulfilled, (state, action) => {
        state.loading = false;
        state.current = action.payload;
      })
      .addCase(fetchWorkspace.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message || 'Failed to fetch workspace';
      });
  }
});

// Analytics slice
interface AnalyticsState {
  stats: GlobalStats | null;
  loading: boolean;
}

const initialAnalyticsState: AnalyticsState = {
  stats: null,
  loading: false
};

export const fetchGlobalStats = createAsyncThunk('analytics/fetchGlobal', async () => {
  const response = await analyticsApi.getGlobalStats();
  return response.data;
});

const analyticsSlice = createSlice({
  name: 'analytics',
  initialState: initialAnalyticsState,
  reducers: {},
  extraReducers: (builder) => {
    builder
      .addCase(fetchGlobalStats.pending, (state) => { state.loading = true; })
      .addCase(fetchGlobalStats.fulfilled, (state, action) => {
        state.loading = false;
        state.stats = action.payload;
      })
      .addCase(fetchGlobalStats.rejected, (state) => { state.loading = false; });
  }
});

// Admin Users slice
interface AdminUsersState {
  list: { email: string }[];
  loading: boolean;
}

const initialAdminUsersState: AdminUsersState = {
  list: [],
  loading: false
};

export const fetchAdminUsers = createAsyncThunk('adminUsers/fetchAll', async () => {
  const response = await usersApi.getAll();
  return response.data;
});

const adminUsersSlice = createSlice({
  name: 'adminUsers',
  initialState: initialAdminUsersState,
  reducers: {},
  extraReducers: (builder) => {
    builder
      .addCase(fetchAdminUsers.pending, (state) => { state.loading = true; })
      .addCase(fetchAdminUsers.fulfilled, (state, action) => {
        state.loading = false;
        state.list = action.payload;
      })
      .addCase(fetchAdminUsers.rejected, (state) => { state.loading = false; });
  }
});

// On-Prem slice
interface OnPremState {
  installs: Install[];
  summary: InstallSummary | null;
  keys: RegistrationKey[];
  loading: boolean;
}

const initialOnPremState: OnPremState = {
  installs: [],
  summary: null,
  keys: [],
  loading: false
};

export const fetchInstalls = createAsyncThunk('onprem/fetchInstalls', async () => {
  const response = await installsApi.getAll();
  return response.data;
});

export const fetchInstallSummary = createAsyncThunk('onprem/fetchSummary', async () => {
  const response = await installsApi.getSummary();
  return response.data;
});

export const fetchRegistrationKeys = createAsyncThunk('onprem/fetchKeys', async () => {
  const response = await keysApi.getAll();
  return response.data;
});

const onPremSlice = createSlice({
  name: 'onprem',
  initialState: initialOnPremState,
  reducers: {},
  extraReducers: (builder) => {
    builder
      .addCase(fetchInstalls.pending, (state) => { state.loading = true; })
      .addCase(fetchInstalls.fulfilled, (state, action) => {
        state.loading = false;
        state.installs = action.payload;
      })
      .addCase(fetchInstalls.rejected, (state) => { state.loading = false; })
      .addCase(fetchInstallSummary.fulfilled, (state, action) => {
        state.summary = action.payload;
      })
      .addCase(fetchRegistrationKeys.fulfilled, (state, action) => {
        state.keys = action.payload;
      });
  }
});

// Store
export const store = configureStore({
  reducer: {
    auth: authSlice.reducer,
    workspaces: workspacesSlice.reducer,
    analytics: analyticsSlice.reducer,
    adminUsers: adminUsersSlice.reducer,
    onprem: onPremSlice.reducer
  }
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;

export const { clearError } = authSlice.actions;
export const { clearCurrent } = workspacesSlice.actions;
