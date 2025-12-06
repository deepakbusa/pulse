import express from 'express';
import cors from 'cors';
import dotenv from 'dotenv';
import http from 'http';
import WebSocket from 'ws';
import { v4 as uuidv4 } from 'uuid';
import bcrypt from 'bcrypt';
import authRoutes from './authRoutes';
import deviceRoutes from './deviceRoutes';
import { WebSocketManager } from './websocketManager';
import { db } from './database';
import { User } from './models';

// Load environment variables
dotenv.config();

// Seed default user if no users exist
function seedDefaultUser() {
  const users = db.getAllUsers();
  if (users.length === 0) {
    // Use a FIXED user ID so tokens remain valid across server restarts
    const defaultUser: User = {
      id: 'default-admin-user-id-12345',
      email: 'admin@pulse.local',
      passwordHash: bcrypt.hashSync('admin123', 10),
      createdAt: new Date()
    };
    db.createUser(defaultUser);
    console.log('✅ Default user created:');
    console.log('   Email: admin@pulse.local');
    console.log('   Password: admin123');
    console.log('   Please change the password after first login!');
  }
}

// Initialize default user
seedDefaultUser();

const app = express();
const PORT = process.env.PORT || 3001;

// Middleware
app.use(cors());
app.use(express.json({ limit: '50mb' })); // Increased limit for image frames

// Routes
app.use('/auth', authRoutes);
app.use('/devices', deviceRoutes);

// Health check
app.get('/health', (req, res) => {
  res.json({ status: 'ok', timestamp: new Date().toISOString() });
});

// Create HTTP server
const server = http.createServer(app);

// Create WebSocket server
const wss = new WebSocket.Server({ server, path: '/ws' });
const wsManager = new WebSocketManager();

wss.on('connection', (ws: WebSocket) => {
  const clientId = uuidv4();
  wsManager.handleConnection(ws, clientId);
});

// Clean up expired pairing codes every 5 minutes
setInterval(() => {
  db.cleanExpiredPairingCodes();
}, 5 * 60 * 1000);

// Start server
server.listen(PORT, () => {
  console.log(`🚀 Pulse Backend Server running on http://localhost:${PORT}`);
  console.log(`📡 WebSocket server available at ws://localhost:${PORT}/ws`);
  console.log(`Environment: ${process.env.NODE_ENV || 'development'}`);
});

// Graceful shutdown
process.on('SIGTERM', () => {
  console.log('SIGTERM received, closing server...');
  server.close(() => {
    console.log('Server closed');
    process.exit(0);
  });
});
