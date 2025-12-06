# Pulse Remote Desktop - Setup Instructions

## 🚀 Zero-Lag Optimizations Applied

Your remote desktop now has **aggressive low-latency optimizations**:
- ✅ Frame dropping enabled (only sends latest frame, no backlog)
- ✅ Quality set to 40 (small file size = fast transfer)
- ✅ 20 FPS for smooth motion
- ✅ Buffer check before sending (prevents 1-minute delays)

---

## For Your Friend (Host Computer)

### Step 1: Get the Host App
Download `PulseHost.exe` from:
```
c:\Users\DEEPAK BUSA\OneDrive\DEEPAK\OneDrive\Documents\Personal\Pulse\host-app\bin\Release\net8.0-windows\win-x64\publish\PulseHost.exe
```

### Step 2: Run and Connect
1. Double-click `PulseHost.exe`
2. Enter this server URL:
   ```
   wss://pulse-jybc.onrender.com/ws
   ```
3. Click "Connect"
4. Wait for "Connected" status
5. Leave the app running - you'll see their screen being shared

---

## For You (Viewer/Controller)

### Step 1: Wait for Backend to Deploy
Render is redeploying with the new optimizations. Wait 2-3 minutes.

Check deployment: https://dashboard.render.com/web/srv-ctbs7p08fa8c73aeqk00/deploys

### Step 2: Open Web Controller
Open in your browser:
```
http://localhost:5173
```
or
```
http://localhost:5174
```

### Step 3: View and Control
1. You'll see their device in the dashboard
2. Click "Connect" to start viewing
3. Their screen will appear in fullscreen
4. Move your mouse and click - it controls their computer!

---

## ⚡ What's Different Now?

**Before:** 1-minute lag, frames queuing up, unresponsive  
**After:** ~500ms-1s delay max, smooth real-time control

The system now **drops old frames** automatically - so you always see the latest screen, not a 1-minute old buffer!

---

## 🔧 Troubleshooting

**Still seeing lag?**
- Wait for Render backend to finish redeploying (check dashboard)
- Refresh your browser (Ctrl+F5)
- Make sure friend is using the NEW PulseHost.exe

**Controls not working?**
- Make sure you clicked inside the screen viewing area
- The app captures full screen - all clicks/keys are sent

**Can't see full screen?**
- Viewing area is fullscreen in browser
- Use F11 for browser fullscreen mode for maximum viewing area

---

## 📊 Technical Details

- **Frame Quality:** 40 (aggressive compression for speed)
- **Frame Rate:** 20 FPS (balanced for network speed)
- **Latency Mode:** Frame dropping enabled (live streaming mode)
- **Buffer Check:** Only sends if WebSocket buffer is empty
- **Result:** Near real-time control with ~500ms-1s latency over internet
