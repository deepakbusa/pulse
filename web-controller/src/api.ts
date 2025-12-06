const API_BASE = import.meta.env.VITE_API_BASE || 'http://localhost:3001';
const WS_BASE = import.meta.env.VITE_WS_BASE || 'ws://localhost:3001';

export interface User {
  id: string;
  email: string;
}

export interface Device {
  id: string;
  name: string;
  status: 'online' | 'offline';
  lastSeen: string;
  osInfo?: string;
  createdAt: string;
}

export interface AuthResponse {
  token: string;
  user: User;
}

class ApiService {
  private token: string | null = null;
  private readonly TOKEN_VERSION = 'v2'; // Increment this to invalidate all old tokens

  constructor() {
    const storedToken = localStorage.getItem('token');
    const storedVersion = localStorage.getItem('tokenVersion');
    
    // Only use token if version matches
    if (storedToken && storedVersion === this.TOKEN_VERSION) {
      this.token = storedToken;
    } else {
      // Clear old token with wrong version
      localStorage.removeItem('token');
      localStorage.removeItem('tokenVersion');
      this.token = null;
    }
  }

  setToken(token: string) {
    this.token = token;
    localStorage.setItem('token', token);
    localStorage.setItem('tokenVersion', this.TOKEN_VERSION);
  }

  clearToken() {
    this.token = null;
    localStorage.removeItem('token');
    localStorage.removeItem('tokenVersion');
  }

  getToken(): string | null {
    return this.token;
  }

  private async request<T>(
    endpoint: string,
    options: RequestInit = {}
  ): Promise<T> {
    const headers: HeadersInit = {
      'Content-Type': 'application/json',
      ...options.headers,
    };

    if (this.token) {
      headers['Authorization'] = `Bearer ${this.token}`;
    }

    const response = await fetch(`${API_BASE}${endpoint}`, {
      ...options,
      headers,
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ error: 'Unknown error' }));
      throw new Error(error.error || `HTTP ${response.status}`);
    }

    return response.json();
  }

  // Auth endpoints
  async register(email: string, password: string): Promise<AuthResponse> {
    return this.request<AuthResponse>('/auth/register', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    });
  }

  async login(email: string, password: string): Promise<AuthResponse> {
    return this.request<AuthResponse>('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    });
  }

  async getCurrentUser(): Promise<User> {
    return this.request<User>('/auth/me');
  }

  // Device endpoints
  async getDevices(): Promise<Device[]> {
    return this.request<Device[]>('/devices');
  }

  async startPairing(): Promise<{ pairingCode: string; expiresAt: string }> {
    return this.request('/devices/pair/start', {
      method: 'POST',
    });
  }

  async deleteDevice(deviceId: string): Promise<void> {
    await this.request(`/devices/${deviceId}`, {
      method: 'DELETE',
    });
  }

  // WebSocket connection
  createWebSocket(): WebSocket {
    const ws = new WebSocket(`${WS_BASE}/ws`);
    return ws;
  }
}

export const apiService = new ApiService();
