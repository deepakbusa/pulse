# Pulse - Remote Desktop Solution

A production-ready remote desktop solution consisting of a backend server, web-based controller, and portable Windows host application.

## ⚠️ IMPORTANT: Ethical Use Only

This software is designed for **legitimate remote desktop access with explicit consent**. The host application:

- **MUST** be run voluntarily by the host user
- Displays a clear UI showing when remote access is active
- Allows the host user to deny or disconnect at any time
- Does NOT operate stealthily or bypass security measures
- Requires authentication and pairing for all connections

**Misuse of this software for unauthorized access, surveillance, or malicious purposes is strictly prohibited and may be illegal.**

## Architecture

### Components

1. **Backend Server** (`/backend`)
   - Node.js/Express REST API
   - WebSocket server for real-time communication
   - User authentication (JWT)
   - Device registration and session management

2. **Web Controller** (`/web-controller`)
   - React web application
   - User dashboard for managing devices
   - Real-time remote desktop viewing and control
   - Responsive UI built with Vite

3. **Windows Host App** (`/host-app`)
   - C# WPF application
   - Portable executable (no installation required)
   - Screen capture and streaming
   - Remote input processing (mouse/keyboard)
   - Optional startup on login

## Features

✅ **User Authentication** - Secure login/register system  
✅ **Device Pairing** - Simple 6-digit pairing codes  
✅ **Real-time Screen Sharing** - Live screen streaming via WebSocket  
✅ **Remote Input Control** - Mouse and keyboard control from browser  
✅ **Session Management** - Host approval required for each session  
✅ **Multi-device Support** - Manage multiple host devices from one account  
✅ **Portable Host App** - No installation, runs without admin rights  
✅ **Startup Option** - Optional auto-start on Windows login  
✅ **Quality Controls** - Adjustable frame rate and quality settings

## Quick Start

### Prerequisites

- **Node.js** 18+ (for backend and web controller)
- **.NET 8.0 SDK** (for Windows host app)
- **Windows OS** (for host app)

### 1. Setup Backend

```bash
cd backend
npm install
cp .env.example .env
# Edit .env and set a secure JWT_SECRET
npm run dev
```

The backend will start on `http://localhost:3001`

### 2. Setup Web Controller

```bash
cd web-controller
npm install
npm run dev
```

The web controller will start on `http://localhost:5173`

### 3. Build Host App

```bash
cd host-app
dotnet build
```

For portable single-file executable:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The portable EXE will be in `bin/Release/net8.0-windows/win-x64/publish/PulseHost.exe`

## Usage Guide

### For Controllers (Users)

1. **Register/Login**
   - Open the web controller at `http://localhost:5173`
   - Create an account or login

2. **Add a Device**
   - Click "Add New Device" on the dashboard
   - A 6-digit pairing code will be generated
   - Download the host app (or direct the host user to download it)
   - Share the pairing code with the host user

3. **Connect to Device**
   - Once paired, the device will appear in your dashboard
   - When the device is online, click "Connect"
   - Wait for the host user to accept the connection
   - Control the remote desktop from your browser

4. **During Session**
   - View the remote screen in real-time
   - Use mouse and keyboard as if on the remote machine
   - Adjust quality settings if needed
   - Click "End Session" when done

### For Hosts (Remote Machine Users)

1. **First Time Setup**
   - Run `PulseHost.exe` (no installation needed)
   - Enter your device name
   - Enter the 6-digit pairing code from the controller
   - Click "Connect"

2. **Subsequent Use**
   - Run `PulseHost.exe`
   - The app will automatically connect using saved credentials
   - Wait for connection requests

3. **Accepting Connections**
   - When a controller requests access, a dialog appears
   - Review the username requesting access
   - Click "Yes" to allow or "No" to deny

4. **During Session**
   - The window shows "Session in progress"
   - The frame counter shows activity
   - You can close the window or click "Stop Sharing" at any time

5. **Auto-Start (Optional)**
   - Check "Start on Windows login" to auto-launch on boot
   - This creates a shortcut in your Startup folder
   - No admin rights required

## Security Considerations

### Authentication & Authorization
- All users must authenticate with email/password
- JWT tokens for secure API access
- Device tokens for host authentication
- Pairing codes expire after 15 minutes

### Data Protection
- Use HTTPS/WSS in production (configure reverse proxy)
- Store passwords hashed with bcrypt
- Device tokens stored locally on host machine
- No password transmission after initial auth

### User Consent
- Host user must explicitly pair device
- Host user must accept each connection request
- Host can disconnect at any time
- Clear UI shows when remote access is active

## Production Deployment

### Backend

1. Set environment variables:
   ```
   PORT=3001
   JWT_SECRET=<strong-random-string>
   NODE_ENV=production
   ```

2. Build and run:
   ```bash
   npm run build
   npm start
   ```

3. Use a process manager (PM2, systemd) to keep it running

4. Configure reverse proxy (nginx) for HTTPS:
   ```nginx
   location / {
       proxy_pass http://localhost:3001;
   }
   location /ws {
       proxy_pass http://localhost:3001;
       proxy_http_version 1.1;
       proxy_set_header Upgrade $http_upgrade;
       proxy_set_header Connection "upgrade";
   }
   ```

### Web Controller

1. Update API URLs in production build:
   ```bash
   # Set VITE_API_BASE and VITE_WS_BASE in .env
   npm run build
   ```

2. Serve the `dist` folder with any web server (nginx, Apache, etc.)

### Host App

1. Build release version:
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
   ```

2. Distribute `PulseHost.exe` to users
3. Update WebSocket URL in code to production server

## Database

The current implementation uses in-memory storage. For production:

1. Replace `database.ts` with a proper database adapter
2. Recommended: PostgreSQL, MongoDB, or MySQL
3. Update models to use your chosen ORM (Prisma, TypeORM, Mongoose)

## Performance Tuning

### Host App Settings
- **FPS**: Default 15, adjust in `ScreenCaptureService.cs`
- **Quality**: Default 80%, adjust for bandwidth
- **Resolution**: Captured at full screen, consider scaling for slower connections

### Network Optimization
- Consider implementing frame differencing (only send changes)
- Use WebRTC for P2P connection (reduces server load)
- Implement adaptive quality based on latency

## Troubleshooting

### Backend won't start
- Check if port 3001 is available
- Verify Node.js version (18+)
- Check `.env` file exists and is valid

### Web controller can't connect
- Verify backend is running
- Check browser console for errors
- Ensure CORS is properly configured

### Host app connection fails
- Verify backend WebSocket endpoint is accessible
- Check firewall settings
- Ensure pairing code hasn't expired (15 min limit)

### Poor performance
- Reduce FPS in host app
- Lower quality setting
- Check network bandwidth
- Close other bandwidth-intensive applications

## Development

### Running in Development Mode

Terminal 1 - Backend:
```bash
cd backend
npm run dev
```

Terminal 2 - Web Controller:
```bash
cd web-controller
npm run dev
```

Terminal 3 - Host App (in Visual Studio or Rider):
```bash
cd host-app
dotnet run
```

### Code Structure

**Backend:**
- `src/index.ts` - Main server entry point
- `src/authRoutes.ts` - Authentication endpoints
- `src/deviceRoutes.ts` - Device management endpoints
- `src/websocketManager.ts` - WebSocket connection handling
- `src/database.ts` - Data storage layer
- `src/models.ts` - Data models

**Web Controller:**
- `src/App.tsx` - Main application component
- `src/AuthContext.tsx` - Authentication state management
- `src/api.ts` - API client
- `src/components/Auth.tsx` - Login/register page
- `src/components/Dashboard.tsx` - Device dashboard
- `src/components/Session.tsx` - Remote desktop viewer

**Host App:**
- `MainWindow.xaml.cs` - Main UI and coordination
- `WebSocketClient.cs` - WebSocket communication
- `ScreenCaptureService.cs` - Screen capture implementation
- `InputSimulator.cs` - Mouse/keyboard input processing

## License

MIT License - See LICENSE file for details

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

Ensure all features maintain the ethical use principles outlined above.

## Support

For issues, questions, or feature requests, please open an issue on the repository.

---

**Remember: Use this software responsibly and only with explicit consent from all parties involved.**
"# pulse" 
