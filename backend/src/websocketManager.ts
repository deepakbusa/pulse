import WebSocket from 'ws';
import { v4 as uuidv4 } from 'uuid';
import { db } from './database';
import { Device, Session } from './models';

/**
 * WebSocket message types
 */
export type WSMessage =
  | { type: 'pair'; pairingCode: string; deviceName: string; osInfo?: string }
  | { type: 'authenticate'; deviceToken: string }
  | { type: 'controllerAuth'; token: string }
  | { type: 'controllerConnect'; anonymous: boolean }
  | { type: 'startSession'; deviceId: string }
  | { type: 'joinSession'; sessionId: string }
  | { type: 'sessionRequest'; sessionId: string; userName: string }
  | { type: 'sessionResponse'; sessionId: string; accepted: boolean }
  | { type: 'frame'; sessionId: string; frameNumber: number; imageData: string }
  | { type: 'input'; sessionId: string; inputType: string; data: any }
  | { type: 'endSession'; sessionId: string }
  | { type: 'ping' }
  | { type: 'pong' };

/**
 * Connected WebSocket clients
 */
interface ConnectedClient {
  ws: WebSocket;
  type: 'host' | 'controller';
  deviceId?: string;
  userId?: string;
  sessionId?: string;
}

export class WebSocketManager {
  private clients: Map<string, ConnectedClient> = new Map();
  private deviceConnections: Map<string, string> = new Map(); // deviceId -> clientId
  private userConnections: Map<string, Set<string>> = new Map(); // userId -> Set of clientIds

  handleConnection(ws: WebSocket, clientId: string) {
    console.log(`New WebSocket connection: ${clientId}`);

    const client: ConnectedClient = {
      ws,
      type: 'host' // Will be determined after authentication
    };

    this.clients.set(clientId, client);

    // Send connection acknowledgment
    this.sendMessage(ws, { type: 'connected', clientId });

    // Set a timeout to close connection if not authenticated within 10 seconds
    const authTimeout = setTimeout(() => {
      const currentClient = this.clients.get(clientId);
      if (currentClient && !currentClient.deviceId && !currentClient.userId) {
        console.log(`Client ${clientId} failed to authenticate within timeout`);
        ws.close(4001, 'Authentication timeout');
      }
    }, 10000);

    // Handle messages
    ws.on('message', (data: WebSocket.Data) => {
      try {
        const message = JSON.parse(data.toString()) as WSMessage;
        this.handleMessage(clientId, message);
        
        // Clear auth timeout once authenticated
        const currentClient = this.clients.get(clientId);
        if (currentClient && (currentClient.deviceId || currentClient.userId)) {
          clearTimeout(authTimeout);
        }
      } catch (error) {
        console.error('Failed to parse WebSocket message:', error);
        this.sendError(ws, 'Invalid message format');
      }
    });

    // Handle disconnect
    ws.on('close', () => {
      clearTimeout(authTimeout);
      this.handleDisconnect(clientId);
    });

    // Handle errors
    ws.on('error', (error) => {
      console.error(`WebSocket error for ${clientId}:`, error);
    });

    // Start ping/pong for keep-alive
    const pingInterval = setInterval(() => {
      if (ws.readyState === WebSocket.OPEN) {
        this.sendMessage(ws, { type: 'ping' });
      } else {
        clearInterval(pingInterval);
      }
    }, 30000);
  }

  private handleMessage(clientId: string, message: WSMessage) {
    const client = this.clients.get(clientId);
    if (!client) return;

    switch (message.type) {
      case 'pair':
        this.handlePairing(clientId, client, message);
        break;

      case 'authenticate':
        this.handleHostAuth(clientId, client, message);
        break;

      case 'controllerAuth':
        this.handleControllerAuth(clientId, client, message);
        break;

      case 'controllerConnect':
        this.handleAnonymousController(clientId, client, message);
        break;

      case 'startSession':
        this.handleStartSession(clientId, client, message);
        break;

      case 'joinSession':
        this.handleJoinSession(clientId, client, message);
        break;

      case 'sessionResponse':
        this.handleSessionResponse(clientId, client, message);
        break;

      case 'frame':
        this.handleFrame(clientId, client, message);
        break;

      case 'input':
        this.handleInput(clientId, client, message);
        break;

      case 'endSession':
        this.handleEndSession(clientId, client, message);
        break;

      case 'pong':
        // Keep-alive response, no action needed
        break;

      default:
        console.warn(`Unknown message type from ${clientId}`);
    }
  }

  private handlePairing(clientId: string, client: ConnectedClient, message: any) {
    const { pairingCode, deviceName, osInfo } = message;

    // Validate pairing code
    const pairing = db.getPairingCode(pairingCode);
    if (!pairing || pairing.used || pairing.expiresAt < new Date()) {
      this.sendError(client.ws, 'Invalid or expired pairing code');
      return;
    }

    // Create device
    const deviceToken = uuidv4();
    const device: Device = {
      id: uuidv4(),
      name: deviceName,
      ownerUserId: pairing.userId,
      status: 'online',
      lastSeen: new Date(),
      deviceToken,
      createdAt: new Date(),
      osInfo
    };

    db.createDevice(device);
    db.markPairingCodeUsed(pairingCode);

    // Update client
    client.type = 'host';
    client.deviceId = device.id;
    this.deviceConnections.set(device.id, clientId);

    // Send success response
    this.sendMessage(client.ws, {
      type: 'paired',
      deviceId: device.id,
      deviceToken
    });

    // Notify user's controllers that a new device is online
    this.notifyUserDevicesUpdated(pairing.userId);

    console.log(`Device paired: ${device.id} for user ${pairing.userId}`);
  }

  private handleHostAuth(clientId: string, client: ConnectedClient, message: any) {
    const { deviceToken } = message;

    // Find device by token
    const device = db.getDeviceByToken(deviceToken);
    if (!device) {
      this.sendError(client.ws, 'Invalid device token');
      client.ws.close();
      return;
    }

    // Update device status
    db.updateDevice(device.id, { status: 'online', lastSeen: new Date() });

    // Update client
    client.type = 'host';
    client.deviceId = device.id;
    this.deviceConnections.set(device.id, clientId);

    // Send success response
    this.sendMessage(client.ws, {
      type: 'authenticated',
      deviceId: device.id
    });

    // Notify user's controllers that device is online
    this.notifyUserDevicesUpdated(device.ownerUserId);

    console.log(`Host authenticated: ${device.id}`);
  }

  private handleControllerAuth(clientId: string, client: ConnectedClient, message: any) {
    const { token } = message;

    if (!token) {
      console.error(`Controller auth failed for ${clientId}: No token provided`);
      this.sendError(client.ws, 'Authentication token required');
      client.ws.close();
      return;
    }

    try {
      const jwt = require('jsonwebtoken');
      const JWT_SECRET = process.env.JWT_SECRET || 'your-secret-key-change-in-production';
      console.log(`Controller auth attempt for ${clientId}`);
      console.log(`Using JWT_SECRET: ${JWT_SECRET.substring(0, 20)}...`);
      console.log(`Token (first 50 chars): ${token.substring(0, 50)}...`);
      const decoded = jwt.verify(token, JWT_SECRET) as { userId: string; email: string };

      // Update client
      client.type = 'controller';
      client.userId = decoded.userId;

      // Track user connection
      if (!this.userConnections.has(decoded.userId)) {
        this.userConnections.set(decoded.userId, new Set());
      }
      this.userConnections.get(decoded.userId)!.add(clientId);

      // Send success response with user's devices
      const userDevices = db.getDevicesByOwner(decoded.userId);
      this.sendMessage(client.ws, {
        type: 'authenticated',
        userId: decoded.userId,
        devices: userDevices
      });

      console.log(`Controller authenticated: ${decoded.userId}`);
    } catch (error: any) {
      console.error(`Controller auth failed for ${clientId}:`, error.message);
      this.sendError(client.ws, 'Invalid authentication token');
      client.ws.close();
    }
  }

  private handleAnonymousController(clientId: string, client: ConnectedClient, message: any) {
    // Allow anonymous controller access - no authentication needed
    client.type = 'controller';
    client.userId = 'anonymous-user';

    // Track anonymous connection
    if (!this.userConnections.has('anonymous-user')) {
      this.userConnections.set('anonymous-user', new Set());
    }
    this.userConnections.get('anonymous-user')!.add(clientId);

    // Get all devices (no user filtering)
    const allDevices = db.getAllDevices();
    this.sendMessage(client.ws, {
      type: 'authenticated',
      userId: 'anonymous-user',
      devices: allDevices
    });

    console.log(`Anonymous controller connected: ${clientId}`);
  }

  private handleStartSession(clientId: string, client: ConnectedClient, message: any) {
    if (client.type !== 'controller' || !client.userId) {
      console.error(`Start session failed: Unauthorized client ${clientId}`);
      this.sendError(client.ws, 'Unauthorized');
      return;
    }

    const { deviceId } = message;
    console.log(`Starting session for device ${deviceId} by controller ${clientId}`);

    // Get device
    const device = db.getDeviceById(deviceId);
    if (!device) {
      console.error(`Start session failed: Device ${deviceId} not found`);
      this.sendError(client.ws, 'Device not found');
      return;
    }

    // For anonymous mode, allow all connections
    if (client.userId !== 'anonymous-user' && device.ownerUserId !== client.userId) {
      console.error(`Start session failed: Device ${deviceId} owned by ${device.ownerUserId}, requested by ${client.userId}`);
      this.sendError(client.ws, 'Device not found or unauthorized');
      return;
    }

    // Check if device is online
    if (device.status !== 'online') {
      console.error(`Start session failed: Device ${deviceId} is offline`);
      this.sendError(client.ws, 'Device is offline');
      return;
    }

    // Check if there's already an active session
    const existingSession = db.getActiveSessionByDevice(deviceId);
    if (existingSession) {
      this.sendError(client.ws, 'Device is already in a session');
      return;
    }

    // Create session
    const session: Session = {
      id: uuidv4(),
      userId: client.userId,
      deviceId,
      status: 'pending',
      startedAt: new Date()
    };

    db.createSession(session);
    client.sessionId = session.id;

    // Get user info
    const user = db.getUserById(client.userId);

    // Notify host
    const hostClientId = this.deviceConnections.get(deviceId);
    if (hostClientId) {
      const hostClient = this.clients.get(hostClientId);
      if (hostClient) {
        this.sendMessage(hostClient.ws, {
          type: 'sessionRequest',
          sessionId: session.id,
          userName: user?.email || 'Unknown user'
        });
      }
    }

    console.log(`Session created: ${session.id}`);
  }

  private handleJoinSession(clientId: string, client: ConnectedClient, message: any) {
    if (client.type !== 'controller' || !client.userId) {
      console.error(`Join session failed: Unauthorized client ${clientId}`);
      this.sendError(client.ws, 'Unauthorized');
      return;
    }

    const { sessionId } = message;
    console.log(`Controller ${clientId} joining session ${sessionId}`);

    // Get session
    const session = db.getSessionById(sessionId);
    if (!session) {
      console.error(`Join session failed: Session ${sessionId} not found`);
      this.sendError(client.ws, 'Session not found');
      return;
    }

    // Verify session is active or pending
    if (session.status !== 'active' && session.status !== 'pending') {
      console.error(`Join session failed: Session ${sessionId} is ${session.status}`);
      this.sendError(client.ws, 'Session is not active');
      return;
    }

    // Associate client with session
    client.sessionId = sessionId;
    console.log(`Controller ${clientId} successfully joined session ${sessionId}`);

    // Confirm join
    this.sendMessage(client.ws, {
      type: 'sessionJoined',
      sessionId
    });
  }

  private handleSessionResponse(clientId: string, client: ConnectedClient, message: any) {
    if (client.type !== 'host' || !client.deviceId) {
      this.sendError(client.ws, 'Unauthorized');
      return;
    }

    const { sessionId, accepted } = message;
    const session = db.getSessionById(sessionId);

    if (!session || session.deviceId !== client.deviceId) {
      this.sendError(client.ws, 'Invalid session');
      return;
    }

    if (accepted) {
      db.updateSession(sessionId, { status: 'active' });
      client.sessionId = sessionId;

      // Notify controller
      this.notifySessionStarted(session.userId, sessionId);

      console.log(`Session accepted: ${sessionId}`);
    } else {
      db.updateSession(sessionId, { status: 'declined', endedAt: new Date() });

      // Notify controller
      this.notifySessionDeclined(session.userId, sessionId);

      console.log(`Session declined: ${sessionId}`);
    }
  }

  private frameStats = {
    received: 0,
    forwarded: 0,
    dropped: 0,
    lastLogTime: Date.now()
  };

  private handleFrame(clientId: string, client: ConnectedClient, message: any) {
    if (client.type !== 'host' || !client.sessionId) {
      console.log(`❌ Frame rejected: client type=${client.type}, sessionId=${client.sessionId}`);
      return;
    }

    const { sessionId, frameNumber, imageData } = message;
    const session = db.getSessionById(sessionId);

    if (!session || session.status !== 'active') {
      console.log(`⚠️  Frame rejected: session ${sessionId} not active (status: ${session?.status || 'not found'})`);
      return;
    }

    this.frameStats.received++;

    // Forward frame to controller - ONLY if WebSocket is ready (low latency mode)
    const userClients = this.userConnections.get(session.userId);
    if (userClients) {
      for (const controllerClientId of userClients) {
        const controllerClient = this.clients.get(controllerClientId);
        if (controllerClient && controllerClient.sessionId === sessionId) {
          const bufferSize = controllerClient.ws.bufferedAmount;
          
          // AGGRESSIVE latency check: only send if buffer is completely clear (< 50KB)
          if (controllerClient.ws.readyState === WebSocket.OPEN && bufferSize < 50000) {
            this.sendMessage(controllerClient.ws, {
              type: 'frame',
              sessionId,
              frameNumber,
              imageData
            });
            this.frameStats.forwarded++;
            
            // Log every 100 frames
            if (this.frameStats.forwarded % 100 === 0) {
              console.log(`📊 Backend Stats: Received ${this.frameStats.received}, Forwarded ${this.frameStats.forwarded}, Dropped ${this.frameStats.dropped}`);
            }
          } else {
            // Drop frame if buffer has backlog
            this.frameStats.dropped++;
            
            if (this.frameStats.dropped % 50 === 0) {
              console.log(`⚠️  Frame drop #${this.frameStats.dropped}: Buffer ${bufferSize} bytes, State: ${controllerClient.ws.readyState}`);
            }
          }
          
          // Periodic detailed report every 10 seconds
          if (Date.now() - this.frameStats.lastLogTime >= 10000) {
            const dropRate = this.frameStats.received > 0 ? (this.frameStats.dropped * 100.0 / this.frameStats.received) : 0;
            console.log(`\n📈 Backend 10-Second Report:`);
            console.log(`   Frames Received: ${this.frameStats.received}`);
            console.log(`   Frames Forwarded: ${this.frameStats.forwarded}`);
            console.log(`   Frames Dropped: ${this.frameStats.dropped} (${dropRate.toFixed(1)}%)`);
            console.log(`   Current Buffer: ${bufferSize} bytes\n`);
            this.frameStats.lastLogTime = Date.now();
          }
        }
      }
    } else {
      if (this.frameStats.received % 50 === 0) {
        console.log(`⚠️  No controllers connected for session ${sessionId}`);
      }
    }
  }

  private handleInput(clientId: string, client: ConnectedClient, message: any) {
    if (client.type !== 'controller' || !client.sessionId) {
      return;
    }

    const { sessionId, inputType, data } = message;
    const session = db.getSessionById(sessionId);

    if (!session || session.status !== 'active') {
      return;
    }

    // Forward input to host
    const hostClientId = this.deviceConnections.get(session.deviceId);
    if (hostClientId) {
      const hostClient = this.clients.get(hostClientId);
      if (hostClient && hostClient.sessionId === sessionId) {
        this.sendMessage(hostClient.ws, {
          type: 'input',
          sessionId,
          inputType,
          data
        });
      }
    }
  }

  private handleEndSession(clientId: string, client: ConnectedClient, message: any) {
    const { sessionId } = message;
    const session = db.getSessionById(sessionId);

    if (!session) {
      console.log(`End session: Session ${sessionId} not found`);
      return;
    }

    console.log(`Ending session ${sessionId} (status: ${session.status})`);
    db.updateSession(sessionId, { status: 'ended', endedAt: new Date() });

    // Notify both parties
    const hostClientId = this.deviceConnections.get(session.deviceId);
    if (hostClientId) {
      const hostClient = this.clients.get(hostClientId);
      if (hostClient) {
        hostClient.sessionId = undefined;
        this.sendMessage(hostClient.ws, { type: 'sessionEnded', sessionId });
      }
    }

    const userClients = this.userConnections.get(session.userId);
    if (userClients) {
      for (const controllerClientId of userClients) {
        const controllerClient = this.clients.get(controllerClientId);
        if (controllerClient) {
          controllerClient.sessionId = undefined;
          this.sendMessage(controllerClient.ws, { type: 'sessionEnded', sessionId });
        }
      }
    }

    console.log(`Session ended: ${sessionId}`);
  }

  private handleDisconnect(clientId: string) {
    const client = this.clients.get(clientId);
    if (!client) return;

    console.log(`Client disconnected: ${clientId} (type: ${client.type}, sessionId: ${client.sessionId || 'none'})`);

    // Handle host disconnect
    if (client.type === 'host' && client.deviceId) {
      db.updateDevice(client.deviceId, { status: 'offline', lastSeen: new Date() });
      this.deviceConnections.delete(client.deviceId);

      // Get device to find owner
      const device = db.getDeviceById(client.deviceId);
      if (device) {
        this.notifyUserDevicesUpdated(device.ownerUserId);
      }

      // End any active session
      if (client.sessionId) {
        this.handleEndSession(clientId, client, { sessionId: client.sessionId });
      }
    }

    // Handle controller disconnect
    if (client.type === 'controller' && client.userId) {
      const userClients = this.userConnections.get(client.userId);
      if (userClients) {
        userClients.delete(clientId);
        if (userClients.size === 0) {
          this.userConnections.delete(client.userId);
        }
      }
      
      // Don't auto-end sessions when controllers disconnect
      // Sessions should only end when:
      // 1. Host disconnects
      // 2. Explicit endSession message
      // This allows seamless handoff between Dashboard and Session components
    }

    this.clients.delete(clientId);
  }

  private notifyUserDevicesUpdated(userId: string) {
    const devices = db.getDevicesByOwner(userId);
    const sanitizedDevices = devices.map(device => ({
      id: device.id,
      name: device.name,
      status: device.status,
      lastSeen: device.lastSeen,
      osInfo: device.osInfo,
      createdAt: device.createdAt
    }));

    const userClients = this.userConnections.get(userId);
    if (userClients) {
      for (const clientId of userClients) {
        const client = this.clients.get(clientId);
        if (client) {
          this.sendMessage(client.ws, {
            type: 'devicesUpdated',
            devices: sanitizedDevices
          });
        }
      }
    }
  }

  private notifySessionStarted(userId: string, sessionId: string) {
    const userClients = this.userConnections.get(userId);
    if (userClients) {
      for (const clientId of userClients) {
        const client = this.clients.get(clientId);
        if (client) {
          this.sendMessage(client.ws, {
            type: 'sessionStarted',
            sessionId
          });
        }
      }
    }
  }

  private notifySessionDeclined(userId: string, sessionId: string) {
    const userClients = this.userConnections.get(userId);
    if (userClients) {
      for (const clientId of userClients) {
        const client = this.clients.get(clientId);
        if (client) {
          this.sendMessage(client.ws, {
            type: 'sessionDeclined',
            sessionId
          });
        }
      }
    }
  }

  private sendMessage(ws: WebSocket, message: any) {
    if (ws.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify(message));
    }
  }

  private sendError(ws: WebSocket, error: string) {
    this.sendMessage(ws, { type: 'error', error });
  }
}
