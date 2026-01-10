import { useEffect, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { useNavigate } from 'react-router-dom';
import { Modal, Button, Form } from 'react-bootstrap';
import { RootState, AppDispatch, fetchWorkspaces } from '../../store';
import { workspacesApi } from '../../api/client';

function formatBytes(bytes: number): string {
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  if (bytes === 0) return '0 B';
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  return `${(bytes / Math.pow(1024, i)).toFixed(2)} ${sizes[i]}`;
}

function formatDate(dateStr: string | null): string {
  if (!dateStr) return '-';
  return new Date(dateStr).toLocaleDateString();
}

export default function Workspaces() {
  const dispatch = useDispatch<AppDispatch>();
  const navigate = useNavigate();
  const { list, loading } = useSelector((state: RootState) => state.workspaces);
  const [search, setSearch] = useState('');
  const [showCreate, setShowCreate] = useState(false);
  const [newWorkspace, setNewWorkspace] = useState({ name: '', ownerEmail: '', maxUsers: 5 });
  const [creating, setCreating] = useState(false);

  useEffect(() => {
    dispatch(fetchWorkspaces());
  }, [dispatch]);

  const filtered = list.filter(
    ws =>
      ws.name.toLowerCase().includes(search.toLowerCase()) ||
      ws.ownerUser.toLowerCase().includes(search.toLowerCase())
  );

  const handleCreate = async () => {
    setCreating(true);
    try {
      await workspacesApi.create(newWorkspace.name, newWorkspace.ownerEmail, newWorkspace.maxUsers);
      setShowCreate(false);
      setNewWorkspace({ name: '', ownerEmail: '', maxUsers: 5 });
      dispatch(fetchWorkspaces());
    } catch (err: any) {
      alert(err.response?.data?.error || 'Failed to create workspace');
    } finally {
      setCreating(false);
    }
  };

  return (
    <div>
      <div className="d-flex justify-content-between align-items-center mb-4">
        <h2 className="mb-0">Workspaces</h2>
        <Button variant="primary" onClick={() => setShowCreate(true)}>
          <i className="bi bi-plus-lg me-1"></i>
          New Workspace
        </Button>
      </div>

      <div className="card">
        <div className="card-header">
          <input
            type="text"
            className="form-control"
            placeholder="Search workspaces..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <div className="card-body p-0">
          {loading ? (
            <div className="d-flex justify-content-center p-4">
              <div className="spinner-border text-primary" role="status" />
            </div>
          ) : (
            <table className="table table-hover mb-0">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Owner</th>
                  <th className="text-center">Users</th>
                  <th className="text-center">Docs</th>
                  <th className="text-end">Size</th>
                  <th className="text-center">Created</th>
                  <th className="text-center">Status</th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((ws) => (
                  <tr
                    key={ws.id}
                    className="clickable-row"
                    onClick={() => navigate(`/workspaces/${ws.id}`)}
                  >
                    <td>{ws.name}</td>
                    <td className="text-muted">{ws.ownerUser}</td>
                    <td className="text-center">{ws.userCount} / {ws.maxUsers}</td>
                    <td className="text-center">{ws.documentCount.toLocaleString()}</td>
                    <td className="text-end">{formatBytes(ws.databaseSizeBytes)}</td>
                    <td className="text-center">{formatDate(ws.created)}</td>
                    <td className="text-center">
                      {ws.isDisabled ? (
                        <span className="badge bg-danger">Disabled</span>
                      ) : (
                        <span className="badge bg-success">Active</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
        <div className="card-footer text-muted">
          {filtered.length} workspace{filtered.length !== 1 ? 's' : ''}
        </div>
      </div>

      <Modal show={showCreate} onHide={() => setShowCreate(false)}>
        <Modal.Header closeButton>
          <Modal.Title>Create Workspace</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <Form>
            <Form.Group className="mb-3">
              <Form.Label>Workspace Name</Form.Label>
              <Form.Control
                type="text"
                value={newWorkspace.name}
                onChange={(e) => setNewWorkspace({ ...newWorkspace, name: e.target.value })}
                placeholder="e.g., acme-corp"
              />
            </Form.Group>
            <Form.Group className="mb-3">
              <Form.Label>Owner Email</Form.Label>
              <Form.Control
                type="email"
                value={newWorkspace.ownerEmail}
                onChange={(e) => setNewWorkspace({ ...newWorkspace, ownerEmail: e.target.value })}
                placeholder="admin@example.com"
              />
            </Form.Group>
            <Form.Group className="mb-3">
              <Form.Label>Max Users</Form.Label>
              <Form.Control
                type="number"
                min={1}
                value={newWorkspace.maxUsers}
                onChange={(e) => setNewWorkspace({ ...newWorkspace, maxUsers: parseInt(e.target.value) || 5 })}
              />
            </Form.Group>
          </Form>
        </Modal.Body>
        <Modal.Footer>
          <Button variant="secondary" onClick={() => setShowCreate(false)}>
            Cancel
          </Button>
          <Button variant="primary" onClick={handleCreate} disabled={creating}>
            {creating ? 'Creating...' : 'Create'}
          </Button>
        </Modal.Footer>
      </Modal>
    </div>
  );
}
