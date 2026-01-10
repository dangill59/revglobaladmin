import { useEffect, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { Modal, Button, Form } from 'react-bootstrap';
import { RootState, AppDispatch, fetchAdminUsers } from '../../store';
import { usersApi } from '../../api/client';

export default function Users() {
  const dispatch = useDispatch<AppDispatch>();
  const { list, loading } = useSelector((state: RootState) => state.adminUsers);
  const [showAdd, setShowAdd] = useState(false);
  const [newEmail, setNewEmail] = useState('');
  const [adding, setAdding] = useState(false);
  const [deleteEmail, setDeleteEmail] = useState<string | null>(null);

  useEffect(() => {
    dispatch(fetchAdminUsers());
  }, [dispatch]);

  const handleAdd = async () => {
    if (!newEmail) return;
    setAdding(true);
    try {
      await usersApi.add(newEmail);
      setShowAdd(false);
      setNewEmail('');
      dispatch(fetchAdminUsers());
    } catch (err: any) {
      alert(err.response?.data?.error || 'Failed to add admin');
    } finally {
      setAdding(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteEmail) return;
    try {
      await usersApi.remove(deleteEmail);
      setDeleteEmail(null);
      dispatch(fetchAdminUsers());
    } catch (err: any) {
      alert(err.response?.data?.error || 'Failed to remove admin');
    }
  };

  return (
    <div>
      <div className="d-flex justify-content-between align-items-center mb-4">
        <h2 className="mb-0">Admin Users</h2>
        <Button variant="primary" onClick={() => setShowAdd(true)}>
          <i className="bi bi-plus-lg me-1"></i>
          Add Admin
        </Button>
      </div>

      <div className="card">
        <div className="card-body p-0">
          {loading ? (
            <div className="d-flex justify-content-center p-4">
              <div className="spinner-border text-primary" role="status" />
            </div>
          ) : (
            <table className="table table-hover mb-0">
              <thead>
                <tr>
                  <th>Email</th>
                  <th className="text-end">Actions</th>
                </tr>
              </thead>
              <tbody>
                {list.map((user) => (
                  <tr key={user.email}>
                    <td>
                      <i className="bi bi-person-circle me-2"></i>
                      {user.email}
                    </td>
                    <td className="text-end">
                      <Button
                        variant="outline-danger"
                        size="sm"
                        onClick={() => setDeleteEmail(user.email)}
                      >
                        <i className="bi bi-trash"></i>
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
        <div className="card-footer text-muted">
          {list.length} admin user{list.length !== 1 ? 's' : ''}
        </div>
      </div>

      <Modal show={showAdd} onHide={() => setShowAdd(false)}>
        <Modal.Header closeButton>
          <Modal.Title>Add Admin User</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <Form>
            <Form.Group>
              <Form.Label>Email Address</Form.Label>
              <Form.Control
                type="email"
                value={newEmail}
                onChange={(e) => setNewEmail(e.target.value)}
                placeholder="admin@example.com"
              />
              <Form.Text className="text-muted">
                This user must already exist in the global users database.
              </Form.Text>
            </Form.Group>
          </Form>
        </Modal.Body>
        <Modal.Footer>
          <Button variant="secondary" onClick={() => setShowAdd(false)}>
            Cancel
          </Button>
          <Button variant="primary" onClick={handleAdd} disabled={adding || !newEmail}>
            {adding ? 'Adding...' : 'Add Admin'}
          </Button>
        </Modal.Footer>
      </Modal>

      <Modal show={!!deleteEmail} onHide={() => setDeleteEmail(null)}>
        <Modal.Header closeButton>
          <Modal.Title>Remove Admin</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p>Are you sure you want to remove <strong>{deleteEmail}</strong> as an admin?</p>
          <p className="text-muted">They will no longer be able to access Global Admin.</p>
        </Modal.Body>
        <Modal.Footer>
          <Button variant="secondary" onClick={() => setDeleteEmail(null)}>
            Cancel
          </Button>
          <Button variant="danger" onClick={handleDelete}>
            Remove Admin
          </Button>
        </Modal.Footer>
      </Modal>
    </div>
  );
}
