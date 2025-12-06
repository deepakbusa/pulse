import express, { Response } from 'express';
import { v4 as uuidv4 } from 'uuid';
import { db } from './database';
import { authenticateToken, AuthRequest } from './authRoutes';
import { PairingCode } from './models';

const router = express.Router();

/**
 * GET /devices
 * Get all devices (no authentication required)
 */
router.get('/', (req: express.Request, res: Response) => {
  try {
    const devices = db.getAllDevices();
    
    // Return sanitized device info (without sensitive tokens)
    const sanitizedDevices = devices.map(device => ({
      id: device.id,
      name: device.name,
      status: device.status,
      lastSeen: device.lastSeen,
      osInfo: device.osInfo,
      createdAt: device.createdAt
    }));

    res.json(sanitizedDevices);
  } catch (error) {
    console.error('Get devices error:', error);
    res.status(500).json({ error: 'Internal server error' });
  }
});

/**
 * POST /devices/pair/start
 * Generate a pairing code for adding a new device (no authentication required)
 */
router.post('/pair/start', (req: express.Request, res: Response) => {
  try {
    // Generate a 6-digit pairing code
    const code = Math.floor(100000 + Math.random() * 900000).toString();
    
    const pairingCode: PairingCode = {
      code,
      userId: 'anonymous-user', // No real user needed
      expiresAt: new Date(Date.now() + 15 * 60 * 1000), // 15 minutes
      used: false
    };

    db.createPairingCode(pairingCode);

    res.json({
      pairingCode: code,
      expiresAt: pairingCode.expiresAt
    });
  } catch (error) {
    console.error('Start pairing error:', error);
    res.status(500).json({ error: 'Internal server error' });
  }
});

/**
 * DELETE /devices/:deviceId
 * Remove a device
 */
router.delete('/:deviceId', authenticateToken, (req: AuthRequest, res: Response) => {
  try {
    const { deviceId } = req.params;
    
    const device = db.getDeviceById(deviceId);
    if (!device) {
      return res.status(404).json({ error: 'Device not found' });
    }

    // Verify ownership
    if (device.ownerUserId !== req.user!.userId) {
      return res.status(403).json({ error: 'Unauthorized' });
    }

    db.deleteDevice(deviceId);

    res.json({ message: 'Device removed successfully' });
  } catch (error) {
    console.error('Delete device error:', error);
    res.status(500).json({ error: 'Internal server error' });
  }
});

export default router;
