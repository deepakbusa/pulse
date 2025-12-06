import React, { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { apiService, type Device } from '../api';
import { useAuth } from '../AuthContext';
import './Dashboard.css';

export const Dashboard: React.FC = () => {
  const navigate = useNavigate();
  const [devices, setDevices] = useState<Device[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState('');
  const [showPairing, setShowPairing] = useState(false);
  const [pairingCode, setPairingCode] = useState('');
  const [selectedDevice, setSelectedDevice] = useState<string | null>(null);
  const { user, logout } = useAuth();
  const wsRef = useRef<WebSocket | null>(null);
  const reconnectTimeoutRef = useRef<number | null>(null);
  const reconnectAttemptsRef = useRef<number>(0);

  useEffect(() => {
    loadDevices();
    connectWebSocket();

    return () => {
      if (reconnectTimeoutRef.current) {
        clearTimeout(reconnectTimeoutRef.current);
      }
      if (wsRef.current) {
        wsRef.current.onclose = null; // Prevent reconnection on unmount
        wsRef.current.close();
      }
    };
  }, []);

  const loadDevices = async () => {
    try {
      setIsLoading(true);
      const deviceList = await apiService.getDevices();
      setDevices(deviceList);
      setError('');
    } catch (err: any) {
      setError(err.message || 'Failed to load devices');
    } finally {
      setIsLoading(false);
    }
  };

  const connectWebSocket = () => {
    const ws = apiService.createWebSocket();
    wsRef.current = ws;

    ws.onopen = () => {
      console.log('WebSocket connected');
      reconnectAttemptsRef.current = 0; // Reset reconnect attempts on successful connection
      
      // Send anonymous controller connection (no auth needed)
      ws.send(JSON.stringify({
        type: 'controllerConnect',
        anonymous: true
      }));
    };

    ws.onmessage = (event) => {
      try {
        const message = JSON.parse(event.data);
        
        switch (message.type) {
          case 'authenticated':
            console.log('WebSocket authenticated successfully');
            if (message.devices) {
              setDevices(message.devices);
            }
            break;
          case 'devicesUpdated':
            setDevices(message.devices);
            break;
          case 'sessionStarted':
            // Navigate to session page
            setSelectedDevice(null);
            navigate(`/session/${message.sessionId}`);
            break;
          case 'sessionDeclined':
            alert('The host declined the connection request.');
            break;
          case 'error':
            console.error('WebSocket server error:', message.error);
            if (message.error.includes('token') || message.error.includes('Authentication') || message.error.includes('Invalid')) {
              // Auth error - clear token and logout
              console.log('Invalid token detected, clearing and logging out...');
              apiService.clearToken();
              logout();
              window.location.reload();
            }
            break;
        }
      } catch (err) {
        console.error('Failed to parse WebSocket message:', err);
      }
    };

    ws.onerror = (error) => {
      console.error('WebSocket error:', error);
    };

    ws.onclose = () => {
      console.log('WebSocket disconnected');
      wsRef.current = null;
      
      // Exponential backoff for reconnection
      reconnectAttemptsRef.current++;
      const delay = Math.min(1000 * Math.pow(2, reconnectAttemptsRef.current), 30000); // Max 30 seconds
      
      console.log(`Reconnecting in ${delay/1000}s... (attempt ${reconnectAttemptsRef.current})`);
      
      reconnectTimeoutRef.current = window.setTimeout(() => {
        if (apiService.getToken()) {
          connectWebSocket();
        }
      }, delay);
    };
  };

  const handleStartPairing = async () => {
    try {
      const response = await apiService.startPairing();
      setPairingCode(response.pairingCode);
      setShowPairing(true);
    } catch (err: any) {
      setError(err.message || 'Failed to start pairing');
    }
  };

  const handleConnect = (deviceId: string) => {
    if (wsRef.current && wsRef.current.readyState === WebSocket.OPEN) {
      wsRef.current.send(JSON.stringify({
        type: 'startSession',
        deviceId
      }));
      setSelectedDevice(deviceId);
    } else {
      alert('WebSocket is not connected. Please refresh the page.');
    }
  };

  const handleDeleteDevice = async (deviceId: string) => {
    if (!confirm('Are you sure you want to remove this device?')) {
      return;
    }

    try {
      await apiService.deleteDevice(deviceId);
      await loadDevices();
    } catch (err: any) {
      setError(err.message || 'Failed to delete device');
    }
  };

  return (
    <div className="dashboard-container">
      <header className="dashboard-header">
        <div className="header-content">
          <h1>Pulse Dashboard</h1>
          <div className="header-actions">
            <span className="user-email">{user?.email}</span>
            <button onClick={logout} className="btn btn-secondary">Logout</button>
          </div>
        </div>
      </header>

      <main className="dashboard-main">
        {error && (
          <div className="error-banner">
            {error}
            <button onClick={() => setError('')} className="close-btn">×</button>
          </div>
        )}

        <div className="devices-section">
          <div className="section-header">
            <h2>My Devices</h2>
            <button onClick={handleStartPairing} className="btn btn-primary">
              Add New Device
            </button>
          </div>

          {isLoading ? (
            <div className="loading">Loading devices...</div>
          ) : devices.length === 0 ? (
            <div className="empty-state">
              <p>No devices connected yet.</p>
              <p className="empty-hint">Click "Add New Device" to get started.</p>
            </div>
          ) : (
            <div className="devices-grid">
              {devices.map((device) => (
                <div key={device.id} className="device-card">
                  <div className="device-header">
                    <h3>{device.name}</h3>
                    <span className={`status-badge ${device.status}`}>
                      {device.status}
                    </span>
                  </div>
                  
                  <div className="device-info">
                    {device.osInfo && <p className="device-os">{device.osInfo}</p>}
                    <p className="device-last-seen">
                      Last seen: {new Date(device.lastSeen).toLocaleString()}
                    </p>
                  </div>

                  <div className="device-actions">
                    <button
                      onClick={() => handleConnect(device.id)}
                      disabled={device.status !== 'online' || selectedDevice === device.id}
                      className="btn btn-primary"
                    >
                      {selectedDevice === device.id ? 'Connecting...' : 'Connect'}
                    </button>
                    <button
                      onClick={() => handleDeleteDevice(device.id)}
                      className="btn btn-danger"
                    >
                      Remove
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </main>

      {showPairing && (
        <div className="modal-overlay" onClick={() => setShowPairing(false)}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()}>
            <h2>Add New Device</h2>
            <p className="modal-instructions">
              Download and run the Pulse Host app on the device you want to control.
              Then enter this pairing code:
            </p>
            
            <div className="pairing-code">{pairingCode}</div>
            
            <div className="download-section">
              <p>Download Pulse Host App:</p>
              <a 
                href="/downloads/pulse-host.exe" 
                className="btn btn-primary"
                download
              >
                Download for Windows
              </a>
              <p className="download-note">
                Note: The host app will be built in the host-app folder.
                Deploy it to your server for actual downloads.
              </p>
            </div>

            <button onClick={() => setShowPairing(false)} className="btn btn-secondary">
              Close
            </button>
          </div>
        </div>
      )}
    </div>
  );
};
