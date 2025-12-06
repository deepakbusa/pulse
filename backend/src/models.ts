/**
 * User model
 * Represents a controller user who can manage and access remote devices
 */
export interface User {
  id: string;
  email: string;
  passwordHash: string;
  createdAt: Date;
}

/**
 * Device model
 * Represents a host machine that can be remotely controlled
 */
export interface Device {
  id: string;
  name: string;
  ownerUserId: string;
  status: 'online' | 'offline';
  lastSeen: Date;
  deviceToken: string;
  createdAt: Date;
  osInfo?: string;
}

/**
 * Session model
 * Represents an active remote control session between a user and a device
 */
export interface Session {
  id: string;
  userId: string;
  deviceId: string;
  status: 'pending' | 'active' | 'ended' | 'declined';
  startedAt: Date;
  endedAt?: Date;
}

/**
 * Pairing code model
 * Temporary codes used to pair new devices with user accounts
 */
export interface PairingCode {
  code: string;
  userId: string;
  expiresAt: Date;
  used: boolean;
}
