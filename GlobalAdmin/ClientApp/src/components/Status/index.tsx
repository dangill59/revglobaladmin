import { useEffect, useState } from 'react';
import { Card, Row, Col, Spinner, Badge } from 'react-bootstrap';
import api from '../../api/client';

interface ServiceHealth {
  category: string;
  name: string;
  status: 'healthy' | 'degraded' | 'unhealthy';
  details: string;
}

interface HealthResponse {
  status: 'healthy' | 'degraded' | 'unhealthy';
  timestamp: string;
  services: ServiceHealth[];
}

export default function Status() {
  const [health, setHealth] = useState<HealthResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastChecked, setLastChecked] = useState<Date | null>(null);

  const fetchHealth = async () => {
    setError(null);
    try {
      const response = await api.get<HealthResponse>('/health');
      setHealth(response.data);
      setLastChecked(new Date());
    } catch (err: any) {
      console.error('Failed to fetch health', err);
      setError(err.response?.status === 401 ? 'Please log in to view status' : 'Unable to fetch status');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchHealth();
    // Refresh every 30 seconds
    const interval = setInterval(fetchHealth, 30000);
    return () => clearInterval(interval);
  }, []);

  const getStatusIcon = (status: string) => {
    switch (status) {
      case 'healthy':
        return <span className="status-dot status-healthy"></span>;
      case 'degraded':
        return <span className="status-dot status-degraded"></span>;
      case 'unhealthy':
        return <span className="status-dot status-unhealthy"></span>;
      default:
        return <span className="status-dot"></span>;
    }
  };

  const getStatusBadge = (status: string) => {
    switch (status) {
      case 'healthy':
        return <Badge bg="success">All Systems Operational</Badge>;
      case 'degraded':
        return <Badge bg="warning">Partial Outage</Badge>;
      case 'unhealthy':
        return <Badge bg="danger">Major Outage</Badge>;
      default:
        return <Badge bg="secondary">Unknown</Badge>;
    }
  };

  const getStatusText = (status: string) => {
    switch (status) {
      case 'healthy':
        return 'Operational';
      case 'degraded':
        return 'Degraded';
      case 'unhealthy':
        return 'Down';
      default:
        return 'Unknown';
    }
  };

  if (loading) {
    return (
      <div className="d-flex justify-content-center align-items-center" style={{ minHeight: '400px' }}>
        <Spinner animation="border" variant="primary" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="text-center p-5">
        <h2>System Status</h2>
        <div className="alert alert-warning mt-4">
          <strong>Unable to load status:</strong> {error}
        </div>
        <button className="btn btn-primary" onClick={() => { setLoading(true); fetchHealth(); }}>
          Try Again
        </button>
      </div>
    );
  }

  // Group services by category
  const grouped = (health?.services || []).reduce((acc, svc) => {
    if (!acc[svc.category]) acc[svc.category] = [];
    acc[svc.category].push(svc);
    return acc;
  }, {} as Record<string, ServiceHealth[]>);

  return (
    <div>
      <style>{`
        .status-dot {
          display: inline-block;
          width: 12px;
          height: 12px;
          border-radius: 50%;
          margin-right: 8px;
        }
        .status-healthy { background-color: #198754; }
        .status-degraded { background-color: #ffc107; }
        .status-unhealthy { background-color: #dc3545; }
        .status-card {
          border-left: 4px solid transparent;
          transition: all 0.2s;
        }
        .status-card.healthy { border-left-color: #198754; }
        .status-card.degraded { border-left-color: #ffc107; }
        .status-card.unhealthy { border-left-color: #dc3545; }
        .overall-status {
          text-align: center;
          padding: 2rem;
          border-radius: 8px;
          margin-bottom: 2rem;
        }
        .overall-status.healthy { background: linear-gradient(135deg, #d1e7dd 0%, #badbcc 100%); }
        .overall-status.degraded { background: linear-gradient(135deg, #fff3cd 0%, #ffe69c 100%); }
        .overall-status.unhealthy { background: linear-gradient(135deg, #f8d7da 0%, #f5c2c7 100%); }
        .service-item {
          padding: 0.75rem 1rem;
          border-bottom: 1px solid #eee;
          display: flex;
          justify-content: space-between;
          align-items: center;
        }
        .service-item:last-child { border-bottom: none; }
        .service-details { color: #6c757d; font-size: 0.85rem; }
      `}</style>

      <div className="d-flex justify-content-between align-items-center mb-4">
        <h2>System Status</h2>
        <small className="text-muted">
          Last checked: {lastChecked?.toLocaleTimeString() || 'Never'}
          <button
            className="btn btn-link btn-sm"
            onClick={() => { setLoading(true); fetchHealth(); }}
          >
            Refresh
          </button>
        </small>
      </div>

      {/* Overall Status Banner */}
      <div className={`overall-status ${health?.status || ''}`}>
        <h3 className="mb-2">
          {health?.status === 'healthy' && '✓ '}
          {health?.status === 'degraded' && '⚠ '}
          {health?.status === 'unhealthy' && '✕ '}
          {getStatusBadge(health?.status || '')}
        </h3>
        <p className="mb-0 text-muted">
          {health?.status === 'healthy' && 'All systems are operating normally'}
          {health?.status === 'degraded' && 'Some systems are experiencing issues'}
          {health?.status === 'unhealthy' && 'Critical systems are down'}
        </p>
      </div>

      {/* Services by Category */}
      <Row>
        {Object.entries(grouped).map(([category, services]) => (
          <Col md={6} key={category} className="mb-4">
            <Card className={`status-card ${services.every(s => s.status === 'healthy') ? 'healthy' : services.some(s => s.status === 'unhealthy') ? 'unhealthy' : 'degraded'}`}>
              <Card.Header>
                <strong>{category}</strong>
              </Card.Header>
              <Card.Body className="p-0">
                {services.map((service, idx) => (
                  <div key={idx} className="service-item">
                    <div>
                      {getStatusIcon(service.status)}
                      <span>{service.name}</span>
                    </div>
                    <div className="text-end">
                      <span className={`badge bg-${service.status === 'healthy' ? 'success' : service.status === 'degraded' ? 'warning' : 'danger'}`}>
                        {getStatusText(service.status)}
                      </span>
                      <div className="service-details">{service.details}</div>
                    </div>
                  </div>
                ))}
              </Card.Body>
            </Card>
          </Col>
        ))}
      </Row>

      {/* Legend */}
      <div className="mt-4 p-3 bg-light rounded">
        <small className="text-muted">
          <strong>Legend:</strong>
          <span className="ms-3">{getStatusIcon('healthy')} Operational</span>
          <span className="ms-3">{getStatusIcon('degraded')} Degraded Performance</span>
          <span className="ms-3">{getStatusIcon('unhealthy')} Service Disruption</span>
        </small>
      </div>
    </div>
  );
}
