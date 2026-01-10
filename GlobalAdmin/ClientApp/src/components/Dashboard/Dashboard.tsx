import { useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { useNavigate } from 'react-router-dom';
import { RootState, AppDispatch, fetchGlobalStats } from '../../store';

function formatBytes(bytes: number): string {
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  if (bytes === 0) return '0 B';
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  return `${(bytes / Math.pow(1024, i)).toFixed(2)} ${sizes[i]}`;
}

export default function Dashboard() {
  const dispatch = useDispatch<AppDispatch>();
  const navigate = useNavigate();
  const { stats, loading } = useSelector((state: RootState) => state.analytics);

  useEffect(() => {
    dispatch(fetchGlobalStats());
  }, [dispatch]);

  if (loading || !stats) {
    return (
      <div className="d-flex justify-content-center p-5">
        <div className="spinner-border text-primary" role="status">
          <span className="visually-hidden">Loading...</span>
        </div>
      </div>
    );
  }

  return (
    <div>
      <h2 className="mb-4">Dashboard</h2>

      <div className="row g-4 mb-4">
        <div className="col-md-3">
          <div className="card stat-card primary">
            <div className="card-body">
              <div className="text-muted small">Total Workspaces</div>
              <div className="stat-value">{stats.totalWorkspaces}</div>
            </div>
          </div>
        </div>
        <div className="col-md-3">
          <div className="card stat-card success">
            <div className="card-body">
              <div className="text-muted small">Total Users</div>
              <div className="stat-value">{stats.totalUsers.toLocaleString()}</div>
            </div>
          </div>
        </div>
        <div className="col-md-3">
          <div className="card stat-card warning">
            <div className="card-body">
              <div className="text-muted small">Total Documents</div>
              <div className="stat-value">{stats.totalDocuments.toLocaleString()}</div>
            </div>
          </div>
        </div>
        <div className="col-md-3">
          <div className="card stat-card info">
            <div className="card-body">
              <div className="text-muted small">Database Size</div>
              <div className="stat-value">{formatBytes(stats.totalDatabaseSizeBytes)}</div>
            </div>
          </div>
        </div>
      </div>

      <div className="row g-4">
        <div className="col-md-6">
          <div className="card">
            <div className="card-header">
              <h5 className="mb-0">Top Workspaces by Size</h5>
            </div>
            <div className="card-body p-0">
              <table className="table table-hover mb-0">
                <thead>
                  <tr>
                    <th>Workspace</th>
                    <th className="text-end">Size</th>
                    <th className="text-end">Documents</th>
                  </tr>
                </thead>
                <tbody>
                  {stats.topWorkspacesBySize.map((ws) => (
                    <tr
                      key={ws.workspaceId}
                      className="clickable-row"
                      onClick={() => navigate(`/workspaces/${ws.workspaceId}`)}
                    >
                      <td>{ws.workspaceName}</td>
                      <td className="text-end">{formatBytes(ws.databaseSizeBytes)}</td>
                      <td className="text-end">{ws.documentCount.toLocaleString()}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>

        <div className="col-md-6">
          <div className="card">
            <div className="card-header">
              <h5 className="mb-0">Top Workspaces by Documents</h5>
            </div>
            <div className="card-body p-0">
              <table className="table table-hover mb-0">
                <thead>
                  <tr>
                    <th>Workspace</th>
                    <th className="text-end">Documents</th>
                    <th className="text-end">Users</th>
                  </tr>
                </thead>
                <tbody>
                  {stats.topWorkspacesByDocuments.map((ws) => (
                    <tr
                      key={ws.workspaceId}
                      className="clickable-row"
                      onClick={() => navigate(`/workspaces/${ws.workspaceId}`)}
                    >
                      <td>{ws.workspaceName}</td>
                      <td className="text-end">{ws.documentCount.toLocaleString()}</td>
                      <td className="text-end">{ws.userCount}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
