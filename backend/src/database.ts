import { User, Device, Session, PairingCode } from './models';

/**
 * In-memory data store
 * For production, replace with a proper database (PostgreSQL, MongoDB, etc.)
 */
class DataStore {
  private users: Map<string, User> = new Map();
  private devices: Map<string, Device> = new Map();
  private sessions: Map<string, Session> = new Map();
  private pairingCodes: Map<string, PairingCode> = new Map();
  
  // Index for quick lookups
  private usersByEmail: Map<string, string> = new Map();
  private devicesByOwner: Map<string, string[]> = new Map();

  // User operations
  createUser(user: User): void {
    this.users.set(user.id, user);
    this.usersByEmail.set(user.email, user.id);
  }

  getUserById(id: string): User | undefined {
    return this.users.get(id);
  }

  getUserByEmail(email: string): User | undefined {
    const userId = this.usersByEmail.get(email);
    return userId ? this.users.get(userId) : undefined;
  }

  getAllUsers(): User[] {
    return Array.from(this.users.values());
  }

  // Device operations
  getAllDevices(): Device[] {
    return Array.from(this.devices.values());
  }
  createDevice(device: Device): void {
    this.devices.set(device.id, device);
    
    const ownerDevices = this.devicesByOwner.get(device.ownerUserId) || [];
    ownerDevices.push(device.id);
    this.devicesByOwner.set(device.ownerUserId, ownerDevices);
  }

  getDeviceById(id: string): Device | undefined {
    return this.devices.get(id);
  }

  getDeviceByToken(token: string): Device | undefined {
    for (const device of this.devices.values()) {
      if (device.deviceToken === token) {
        return device;
      }
    }
    return undefined;
  }

  getDevicesByOwner(userId: string): Device[] {
    const deviceIds = this.devicesByOwner.get(userId) || [];
    return deviceIds.map(id => this.devices.get(id)).filter(d => d !== undefined) as Device[];
  }

  updateDevice(id: string, updates: Partial<Device>): void {
    const device = this.devices.get(id);
    if (device) {
      this.devices.set(id, { ...device, ...updates });
    }
  }

  deleteDevice(id: string): void {
    const device = this.devices.get(id);
    if (device) {
      this.devices.delete(id);
      
      const ownerDevices = this.devicesByOwner.get(device.ownerUserId) || [];
      const index = ownerDevices.indexOf(id);
      if (index > -1) {
        ownerDevices.splice(index, 1);
        this.devicesByOwner.set(device.ownerUserId, ownerDevices);
      }
    }
  }

  // Session operations
  createSession(session: Session): void {
    this.sessions.set(session.id, session);
  }

  getSessionById(id: string): Session | undefined {
    return this.sessions.get(id);
  }

  updateSession(id: string, updates: Partial<Session>): void {
    const session = this.sessions.get(id);
    if (session) {
      this.sessions.set(id, { ...session, ...updates });
    }
  }

  getActiveSessionByDevice(deviceId: string): Session | undefined {
    for (const session of this.sessions.values()) {
      if (session.deviceId === deviceId && (session.status === 'active' || session.status === 'pending')) {
        return session;
      }
    }
    return undefined;
  }

  // Pairing code operations
  createPairingCode(pairingCode: PairingCode): void {
    this.pairingCodes.set(pairingCode.code, pairingCode);
  }

  getPairingCode(code: string): PairingCode | undefined {
    return this.pairingCodes.get(code);
  }

  markPairingCodeUsed(code: string): void {
    const pairingCode = this.pairingCodes.get(code);
    if (pairingCode) {
      pairingCode.used = true;
    }
  }

  cleanExpiredPairingCodes(): void {
    const now = new Date();
    for (const [code, pairingCode] of this.pairingCodes.entries()) {
      if (pairingCode.expiresAt < now) {
        this.pairingCodes.delete(code);
      }
    }
  }
}

export const db = new DataStore();
