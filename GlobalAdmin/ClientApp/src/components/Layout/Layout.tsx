import { NavLink, useNavigate } from 'react-router-dom';
import { useDispatch, useSelector } from 'react-redux';
import { RootState, AppDispatch, logout } from '../../store';

interface LayoutProps {
  children: React.ReactNode;
}

export default function Layout({ children }: LayoutProps) {
  const dispatch = useDispatch<AppDispatch>();
  const navigate = useNavigate();
  const { user } = useSelector((state: RootState) => state.auth);

  const handleLogout = async () => {
    await dispatch(logout());
    navigate('/login');
  };

  return (
    <div className="d-flex">
      <div className="sidebar d-flex flex-column">
        <div className="p-3 text-white">
          <h5 className="mb-0">Global Admin</h5>
          <small className="text-muted">ScanRev Management</small>
        </div>
        <hr className="text-white-50 my-0" />
        <nav className="nav flex-column mt-3">
          <NavLink
            to="/"
            end
            className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}
          >
            <i className="bi bi-speedometer2"></i>
            Dashboard
          </NavLink>
          <NavLink
            to="/workspaces"
            className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}
          >
            <i className="bi bi-building"></i>
            Workspaces
          </NavLink>
          <NavLink
            to="/users"
            className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}
          >
            <i className="bi bi-people"></i>
            Admin Users
          </NavLink>
          <NavLink
            to="/onprem"
            className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}
          >
            <i className="bi bi-server"></i>
            On-Prem
          </NavLink>
          <NavLink
            to="/status"
            className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}
          >
            <i className="bi bi-heart-pulse"></i>
            System Status
          </NavLink>
        </nav>
        <div className="mt-auto p-3 border-top border-secondary">
          <div className="text-white-50 small mb-2">
            <i className="bi bi-person-circle me-1"></i>
            {user?.email}
          </div>
          <button
            className="btn btn-outline-light btn-sm w-100"
            onClick={handleLogout}
          >
            <i className="bi bi-box-arrow-right me-1"></i>
            Sign Out
          </button>
        </div>
      </div>
      <main className="main-content">{children}</main>
    </div>
  );
}
