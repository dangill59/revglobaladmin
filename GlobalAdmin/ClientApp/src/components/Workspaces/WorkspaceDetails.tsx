import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useDispatch, useSelector } from 'react-redux';
import { Modal, Button, Form, Tabs, Tab, Badge, Card, Row, Col, ProgressBar, Alert } from 'react-bootstrap';
import { RootState, AppDispatch, fetchWorkspace, clearCurrent } from '../../store';
import { workspacesApi } from '../../api/client';
import type { WorkspaceUser, WorkspaceSettings } from '../../types';

interface IndexingStatus {
  totalDocuments: number;
  indexedDocuments: number;
  pendingIndexing: number;
  percentComplete: number;
}

function formatBytes(bytes: number): string {
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  if (bytes === 0) return '0 B';
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  return `${(bytes / Math.pow(1024, i)).toFixed(2)} ${sizes[i]}`;
}

export default function WorkspaceDetails() {
  const { id } = useParams<{ id: string }>();
  const dispatch = useDispatch<AppDispatch>();
  const navigate = useNavigate();
  const { current: workspace, loading } = useSelector((state: RootState) => state.workspaces);
  const [users, setUsers] = useState<WorkspaceUser[]>([]);
  const [saving, setSaving] = useState(false);
  const [showDelete, setShowDelete] = useState(false);
  const [reindexing, setReindexing] = useState(false);
  const [settings, setSettings] = useState<WorkspaceSettings>({});
  const [indexingStatus, setIndexingStatus] = useState<IndexingStatus | null>(null);
  const [showIndexingStatus, setShowIndexingStatus] = useState(false);

  useEffect(() => {
    if (id) {
      dispatch(fetchWorkspace(id));
      workspacesApi.getUsers(id).then(res => setUsers(res.data));
    }
    return () => { dispatch(clearCurrent()); };
  }, [id, dispatch]);

  useEffect(() => {
    if (workspace) {
      setSettings({
        maxUsers: workspace.stats.licenseCount,
        // Feature toggles only - config is managed by workspace admins
        featureFullTextOcr: workspace.settings.featureFullTextOcr,
        featureBarcode: workspace.settings.featureBarcode,
        featureScripts: workspace.settings.featureScripts,
        featureTwoFactor: workspace.settings.featureTwoFactor,
        softDeleteEnabled: workspace.settings.softDeleteEnabled,
        customBrandingEnabled: workspace.settings.customBrandingEnabled,
        auditLogsEnabled: workspace.settings.auditLogsEnabled,
        matchMergeEnabled: workspace.settings.matchMergeEnabled,
      });
    }
  }, [workspace]);

  const handleSave = async () => {
    if (!id) return;
    setSaving(true);
    try {
      await workspacesApi.updateSettings(id, settings);
      dispatch(fetchWorkspace(id));
      alert('Settings saved');
    } catch (err: any) {
      alert(err.response?.data?.error || 'Failed to save');
    } finally {
      setSaving(false);
    }
  };

  const handleToggleStatus = async () => {
    if (!id || !workspace) return;
    try {
      if (workspace.isDisabled) {
        await workspacesApi.enable(id);
      } else {
        await workspacesApi.disable(id);
      }
      dispatch(fetchWorkspace(id));
    } catch (err: any) {
      alert(err.response?.data?.error || 'Failed to update status');
    }
  };

  const handleDelete = async () => {
    if (!id) return;
    try {
      await workspacesApi.delete(id);
      navigate('/workspaces');
    } catch (err: any) {
      alert(err.response?.data?.error || 'Failed to delete');
    }
  };

  const fetchIndexingStatus = useCallback(async () => {
    if (!id) return;
    try {
      const res = await workspacesApi.getIndexingStatus(id);
      setIndexingStatus(res.data);
    } catch (err) {
      console.error('Failed to fetch indexing status', err);
    }
  }, [id]);

  // Poll for indexing status when showing
  useEffect(() => {
    if (!showIndexingStatus || !id) return;

    fetchIndexingStatus();
    const interval = setInterval(fetchIndexingStatus, 3000);

    return () => clearInterval(interval);
  }, [showIndexingStatus, id, fetchIndexingStatus]);

  const handleReindex = async () => {
    if (!id) return;
    setReindexing(true);
    try {
      await workspacesApi.triggerReindex(id);
      setShowIndexingStatus(true);
      fetchIndexingStatus();
    } catch (err: any) {
      alert(err.response?.data?.error || 'Failed to trigger reindex');
    } finally {
      setReindexing(false);
    }
  };

  if (loading || !workspace) {
    return (
      <div className="d-flex justify-content-center p-5">
        <div className="spinner-border text-primary" role="status" />
      </div>
    );
  }

  return (
    <div>
      <div className="d-flex justify-content-between align-items-center mb-4">
        <div>
          <button className="btn btn-link p-0 mb-2" onClick={() => navigate('/workspaces')}>
            &larr; Back to Workspaces
          </button>
          <h2 className="mb-0">
            {workspace.name}
            {workspace.isDisabled && <Badge bg="danger" className="ms-2">Disabled</Badge>}
          </h2>
          <small className="text-muted">{workspace.ownerUser}</small>
        </div>
        <div>
          <Button
            variant="info"
            className="me-2"
            onClick={handleReindex}
            disabled={reindexing}
          >
            {reindexing ? 'Triggering...' : 'Trigger Reindex'}
          </Button>
          <Button
            variant={workspace.isDisabled ? 'success' : 'warning'}
            className="me-2"
            onClick={handleToggleStatus}
          >
            {workspace.isDisabled ? 'Enable' : 'Disable'}
          </Button>
          <Button variant="danger" onClick={() => setShowDelete(true)}>
            Delete
          </Button>
        </div>
      </div>

      <div className="row g-4 mb-4">
        <div className="col-md-3">
          <div className="card stat-card primary">
            <div className="card-body">
              <div className="text-muted small">Users</div>
              <div className="stat-value">{workspace.stats.userCount} / {workspace.stats.licenseCount}</div>
            </div>
          </div>
        </div>
        <div className="col-md-3">
          <div className="card stat-card success">
            <div className="card-body">
              <div className="text-muted small">Documents</div>
              <div className="stat-value">{workspace.stats.documentCount.toLocaleString()}</div>
            </div>
          </div>
        </div>
        <div className="col-md-3">
          <div className="card stat-card warning">
            <div className="card-body">
              <div className="text-muted small">Database Size</div>
              <div className="stat-value">{formatBytes(workspace.stats.databaseSizeBytes)}</div>
            </div>
          </div>
        </div>
        <div className="col-md-3">
          <div className="card stat-card info">
            <div className="card-body">
              <div className="text-muted small">Storage Size</div>
              <div className="stat-value">{formatBytes(workspace.stats.storageSizeBytes)}</div>
            </div>
          </div>
        </div>
      </div>

      {showIndexingStatus && indexingStatus && (
        <Alert
          variant={indexingStatus.pendingIndexing === 0 ? 'success' : 'info'}
          dismissible
          onClose={() => setShowIndexingStatus(false)}
          className="mb-4"
        >
          <Alert.Heading>
            {indexingStatus.pendingIndexing === 0 ? 'Indexing Complete' : 'Indexing in Progress'}
          </Alert.Heading>
          <ProgressBar
            now={indexingStatus.percentComplete}
            label={`${indexingStatus.percentComplete}%`}
            animated={indexingStatus.pendingIndexing > 0}
            variant={indexingStatus.pendingIndexing === 0 ? 'success' : 'info'}
            className="mb-2"
          />
          <small>
            {indexingStatus.indexedDocuments.toLocaleString()} / {indexingStatus.totalDocuments.toLocaleString()} documents indexed
            {indexingStatus.pendingIndexing > 0 && ` (${indexingStatus.pendingIndexing.toLocaleString()} pending)`}
          </small>
        </Alert>
      )}

      <div className="card">
        <div className="card-body">
          <Tabs defaultActiveKey="features" className="mb-3">
            <Tab eventKey="features" title="Features">
              <Row>
                <Col md={6}>
                  <Card className="mb-3">
                    <Card.Header>License</Card.Header>
                    <Card.Body>
                      <Form.Group>
                        <Form.Label>Maximum Users</Form.Label>
                        <Form.Control
                          type="number"
                          min={1}
                          value={settings.maxUsers || 5}
                          onChange={(e) => setSettings({ ...settings, maxUsers: parseInt(e.target.value) })}
                        />
                        <Form.Text className="text-muted">
                          Current: {workspace.stats.userCount} users
                        </Form.Text>
                      </Form.Group>
                    </Card.Body>
                  </Card>

                  <Card className="mb-3">
                    <Card.Header>Document Processing</Card.Header>
                    <Card.Body>
                      <Form.Check
                        type="switch"
                        id="featureFullTextOcr"
                        label="Full-Text OCR & Search"
                        checked={settings.featureFullTextOcr || false}
                        onChange={(e) => setSettings({ ...settings, featureFullTextOcr: e.target.checked })}
                        className="mb-3"
                      />

                      <Form.Check
                        type="switch"
                        id="featureBarcode"
                        label="Barcode Detection"
                        checked={settings.featureBarcode || false}
                        onChange={(e) => setSettings({ ...settings, featureBarcode: e.target.checked })}
                        className="mb-3"
                      />

                      <Form.Check
                        type="switch"
                        id="featureScripts"
                        label="Automation Scripts"
                        checked={settings.featureScripts || false}
                        onChange={(e) => setSettings({ ...settings, featureScripts: e.target.checked })}
                      />
                    </Card.Body>
                  </Card>
                </Col>

                <Col md={6}>
                  <Card className="mb-3">
                    <Card.Header>Data Management</Card.Header>
                    <Card.Body>
                      <Form.Check
                        type="switch"
                        id="softDeleteEnabled"
                        label="Soft Delete (Trash)"
                        checked={settings.softDeleteEnabled || false}
                        onChange={(e) => setSettings({ ...settings, softDeleteEnabled: e.target.checked })}
                      />
                      <Form.Text className="text-muted d-block mb-3">
                        Workspace admins configure retention settings
                      </Form.Text>

                      <Form.Check
                        type="switch"
                        id="auditLogsEnabled"
                        label="Audit Logs"
                        checked={settings.auditLogsEnabled || false}
                        onChange={(e) => setSettings({ ...settings, auditLogsEnabled: e.target.checked })}
                      />
                      <Form.Text className="text-muted d-block mb-3">
                        Track user actions and document changes
                      </Form.Text>

                      <Form.Check
                        type="switch"
                        id="matchMergeEnabled"
                        label="Match & Merge"
                        checked={settings.matchMergeEnabled || false}
                        onChange={(e) => setSettings({ ...settings, matchMergeEnabled: e.target.checked })}
                      />
                      <Form.Text className="text-muted d-block">
                        Sync document fields from external data sources
                      </Form.Text>
                    </Card.Body>
                  </Card>

                  <Card className="mb-3">
                    <Card.Header>Security & Branding</Card.Header>
                    <Card.Body>
                      <Form.Check
                        type="switch"
                        id="featureTwoFactor"
                        label="Two-Factor Authentication"
                        checked={settings.featureTwoFactor || false}
                        onChange={(e) => setSettings({ ...settings, featureTwoFactor: e.target.checked })}
                        className="mb-3"
                      />

                      <Form.Check
                        type="switch"
                        id="customBrandingEnabled"
                        label="Custom Branding"
                        checked={settings.customBrandingEnabled || false}
                        onChange={(e) => setSettings({ ...settings, customBrandingEnabled: e.target.checked })}
                      />
                      <Form.Text className="text-muted d-block">
                        Workspace admins configure logo and colors
                      </Form.Text>
                    </Card.Body>
                  </Card>
                </Col>
              </Row>

              <div className="mt-4">
                <Button variant="primary" onClick={handleSave} disabled={saving}>
                  {saving ? 'Saving...' : 'Save Settings'}
                </Button>
              </div>
            </Tab>

            <Tab eventKey="users" title={`Users (${users.length})`}>
              <table className="table">
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Email</th>
                    <th>Role</th>
                  </tr>
                </thead>
                <tbody>
                  {users.map((user) => (
                    <tr key={user.id}>
                      <td>{user.preferredName || user.userName}</td>
                      <td>{user.emailAddress}</td>
                      <td>
                        {user.isAdmin ? (
                          <Badge bg="primary">Admin</Badge>
                        ) : (
                          <Badge bg="secondary">User</Badge>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Tab>
          </Tabs>
        </div>
      </div>

      <Modal show={showDelete} onHide={() => setShowDelete(false)}>
        <Modal.Header closeButton>
          <Modal.Title>Delete Workspace</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p>Are you sure you want to delete <strong>{workspace.name}</strong>?</p>
          <p className="text-danger">
            This will permanently delete:
          </p>
          <ul>
            <li>{workspace.stats.documentCount.toLocaleString()} documents</li>
            <li>{workspace.stats.userCount} users</li>
            <li>All files in cloud storage</li>
            <li>All search indexes</li>
          </ul>
          <p><strong>This action cannot be undone.</strong></p>
        </Modal.Body>
        <Modal.Footer>
          <Button variant="secondary" onClick={() => setShowDelete(false)}>
            Cancel
          </Button>
          <Button variant="danger" onClick={handleDelete}>
            Delete Workspace
          </Button>
        </Modal.Footer>
      </Modal>
    </div>
  );
}
