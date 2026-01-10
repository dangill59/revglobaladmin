import { useEffect, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { Modal, Button, Form, Tabs, Tab, Badge } from 'react-bootstrap';
import { RootState, AppDispatch, fetchInstalls, fetchInstallSummary, fetchRegistrationKeys } from '../../store';
import { keysApi } from '../../api/client';

function formatBytes(bytes: number): string {
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  if (bytes === 0) return '0 B';
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  return `${(bytes / Math.pow(1024, i)).toFixed(2)} ${sizes[i]}`;
}

function formatDate(dateStr: string): string {
  return new Date(dateStr).toLocaleString();
}

function getStatusBadge(status: string) {
  switch (status) {
    case 'healthy':
      return <Badge bg="success">Healthy</Badge>;
    case 'warning':
      return <Badge bg="warning">Warning</Badge>;
    case 'offline':
      return <Badge bg="danger">Offline</Badge>;
    default:
      return <Badge bg="secondary">{status}</Badge>;
  }
}

export default function OnPrem() {
  const dispatch = useDispatch<AppDispatch>();
  const { installs, summary, keys, loading } = useSelector((state: RootState) => state.onprem);
  const [showGenerate, setShowGenerate] = useState(false);
  const [generating, setGenerating] = useState(false);
  const [newKey, setNewKey] = useState({
    customerName: '',
    contactEmail: '',
    maxUsers: 5,
    tier: 'standard',
    validityDays: 30
  });

  useEffect(() => {
    dispatch(fetchInstalls());
    dispatch(fetchInstallSummary());
    dispatch(fetchRegistrationKeys());
  }, [dispatch]);

  const handleGenerate = async () => {
    setGenerating(true);
    try {
      await keysApi.generate(newKey);
      setShowGenerate(false);
      setNewKey({ customerName: '', contactEmail: '', maxUsers: 5, tier: 'standard', validityDays: 30 });
      dispatch(fetchRegistrationKeys());
    } catch (err: any) {
      alert(err.response?.data?.error || 'Failed to generate key');
    } finally {
      setGenerating(false);
    }
  };

  const handleRevoke = async (key: string) => {
    if (!confirm('Are you sure you want to revoke this key?')) return;
    try {
      await keysApi.revoke(key);
      dispatch(fetchRegistrationKeys());
    } catch (err: any) {
      alert(err.response?.data?.error || 'Failed to revoke key');
    }
  };

  return (
    <div>
      <h2 className="mb-4">On-Premises Management</h2>

      {summary && (
        <div className="row g-4 mb-4">
          <div className="col-md-3">
            <div className="card stat-card primary">
              <div className="card-body">
                <div className="text-muted small">Total Installations</div>
                <div className="stat-value">{summary.total}</div>
              </div>
            </div>
          </div>
          <div className="col-md-3">
            <div className="card stat-card success">
              <div className="card-body">
                <div className="text-muted small">Healthy</div>
                <div className="stat-value">{summary.healthy}</div>
              </div>
            </div>
          </div>
          <div className="col-md-3">
            <div className="card stat-card warning">
              <div className="card-body">
                <div className="text-muted small">Warning</div>
                <div className="stat-value">{summary.warning}</div>
              </div>
            </div>
          </div>
          <div className="col-md-3">
            <div className="card stat-card info">
              <div className="card-body">
                <div className="text-muted small">Total Documents</div>
                <div className="stat-value">{summary.totalDocuments.toLocaleString()}</div>
              </div>
            </div>
          </div>
        </div>
      )}

      <div className="card">
        <div className="card-body">
          <Tabs defaultActiveKey="installations" className="mb-3">
            <Tab eventKey="installations" title={`Installations (${installs.length})`}>
              {loading ? (
                <div className="d-flex justify-content-center p-4">
                  <div className="spinner-border text-primary" role="status" />
                </div>
              ) : (
                <table className="table table-hover">
                  <thead>
                    <tr>
                      <th>Customer</th>
                      <th>Version</th>
                      <th>Status</th>
                      <th className="text-end">Users</th>
                      <th className="text-end">Documents</th>
                      <th className="text-end">Storage</th>
                      <th>Last Heartbeat</th>
                    </tr>
                  </thead>
                  <tbody>
                    {installs.map((install) => (
                      <tr key={install.id}>
                        <td>
                          <div>{install.customerName}</div>
                          <small className="text-muted">{install.contactEmail}</small>
                        </td>
                        <td>{install.version}</td>
                        <td>{getStatusBadge(install.status)}</td>
                        <td className="text-end">
                          {install.metrics?.activeUsers ?? '-'} / {install.license?.maxUsers ?? '-'}
                        </td>
                        <td className="text-end">
                          {install.metrics?.totalDocuments?.toLocaleString() ?? '-'}
                        </td>
                        <td className="text-end">
                          {install.metrics?.storageUsedBytes
                            ? formatBytes(install.metrics.storageUsedBytes)
                            : '-'}
                        </td>
                        <td>
                          <small>{install.lastHeartbeat ? formatDate(install.lastHeartbeat) : 'Never'}</small>
                        </td>
                      </tr>
                    ))}
                    {installs.length === 0 && (
                      <tr>
                        <td colSpan={7} className="text-center text-muted py-4">
                          No on-prem installations registered
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              )}
            </Tab>

            <Tab
              eventKey="keys"
              title={
                <>
                  Registration Keys
                  {keys.length > 0 && <Badge bg="secondary" className="ms-2">{keys.length}</Badge>}
                </>
              }
            >
              <div className="mb-3">
                <Button variant="primary" onClick={() => setShowGenerate(true)}>
                  <i className="bi bi-plus-lg me-1"></i>
                  Generate Key
                </Button>
              </div>

              <table className="table table-hover">
                <thead>
                  <tr>
                    <th>Key</th>
                    <th>Customer</th>
                    <th>License</th>
                    <th>Expires</th>
                    <th className="text-end">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {keys.map((key) => (
                    <tr key={key.key}>
                      <td>
                        <code>{key.key}</code>
                      </td>
                      <td>
                        <div>{key.customerName}</div>
                        <small className="text-muted">{key.contactEmail}</small>
                      </td>
                      <td>
                        <div>{key.maxUsers} users</div>
                        <small className="text-muted">{key.tier}</small>
                      </td>
                      <td>{formatDate(key.expiresAt)}</td>
                      <td className="text-end">
                        <Button
                          variant="outline-danger"
                          size="sm"
                          onClick={() => handleRevoke(key.key)}
                        >
                          Revoke
                        </Button>
                      </td>
                    </tr>
                  ))}
                  {keys.length === 0 && (
                    <tr>
                      <td colSpan={5} className="text-center text-muted py-4">
                        No unused registration keys
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </Tab>
          </Tabs>
        </div>
      </div>

      <Modal show={showGenerate} onHide={() => setShowGenerate(false)}>
        <Modal.Header closeButton>
          <Modal.Title>Generate Registration Key</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <Form>
            <Form.Group className="mb-3">
              <Form.Label>Customer Name</Form.Label>
              <Form.Control
                type="text"
                value={newKey.customerName}
                onChange={(e) => setNewKey({ ...newKey, customerName: e.target.value })}
                placeholder="ACME Corp"
              />
            </Form.Group>
            <Form.Group className="mb-3">
              <Form.Label>Contact Email</Form.Label>
              <Form.Control
                type="email"
                value={newKey.contactEmail}
                onChange={(e) => setNewKey({ ...newKey, contactEmail: e.target.value })}
                placeholder="contact@acme.com"
              />
            </Form.Group>
            <div className="row">
              <div className="col-md-6">
                <Form.Group className="mb-3">
                  <Form.Label>Max Users</Form.Label>
                  <Form.Control
                    type="number"
                    min={1}
                    value={newKey.maxUsers}
                    onChange={(e) => setNewKey({ ...newKey, maxUsers: parseInt(e.target.value) || 5 })}
                  />
                </Form.Group>
              </div>
              <div className="col-md-6">
                <Form.Group className="mb-3">
                  <Form.Label>Tier</Form.Label>
                  <Form.Select
                    value={newKey.tier}
                    onChange={(e) => setNewKey({ ...newKey, tier: e.target.value })}
                  >
                    <option value="standard">Standard</option>
                    <option value="professional">Professional</option>
                    <option value="enterprise">Enterprise</option>
                  </Form.Select>
                </Form.Group>
              </div>
            </div>
            <Form.Group className="mb-3">
              <Form.Label>Validity (days)</Form.Label>
              <Form.Control
                type="number"
                min={1}
                value={newKey.validityDays}
                onChange={(e) => setNewKey({ ...newKey, validityDays: parseInt(e.target.value) || 30 })}
              />
            </Form.Group>
          </Form>
        </Modal.Body>
        <Modal.Footer>
          <Button variant="secondary" onClick={() => setShowGenerate(false)}>
            Cancel
          </Button>
          <Button
            variant="primary"
            onClick={handleGenerate}
            disabled={generating || !newKey.customerName || !newKey.contactEmail}
          >
            {generating ? 'Generating...' : 'Generate Key'}
          </Button>
        </Modal.Footer>
      </Modal>
    </div>
  );
}
