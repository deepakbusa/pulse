# Deployment Guide for Render

## 🚀 Deploy Backend to Render

### Step 1: Push to GitHub
Your code is already on GitHub at: https://github.com/deepakbusa/pulse

### Step 2: Create Render Account
1. Go to https://render.com
2. Sign up/login with your GitHub account

### Step 3: Create New Web Service
1. Click "New +" → "Web Service"
2. Connect your GitHub account if not already connected
3. Select the `pulse` repository
4. Configure the service:

   **Settings:**
   - Name: `pulse-backend` (or any name you want)
   - Region: Choose closest to you
   - Branch: `main`
   - Root Directory: `backend`
   - Runtime: `Node`
   - Build Command: `npm run render-build`
   - Start Command: `npm start`
   - Instance Type: `Free` (for testing)

### Step 4: Add Environment Variables
Click "Environment" and add:
- `PORT` = `10000` (Render uses port 10000)
- `JWT_SECRET` = `pulse-secret-key-2025-production`
- `NODE_ENV` = `production`

### Step 5: Deploy
1. Click "Create Web Service"
2. Wait for deployment (5-10 minutes)
3. Your backend URL will be: `https://pulse-backend.onrender.com`

### Step 6: Update Web Controller
After backend is deployed, update the API URL in your web controller:

File: `web-controller/src/api.ts`
```typescript
const API_URL = 'https://pulse-backend.onrender.com';
const WS_URL = 'wss://pulse-backend.onrender.com/ws';
```

### Step 7: Deploy Web Controller to Vercel
1. Go to https://vercel.com
2. Import the `pulse` repository
3. Set Root Directory: `web-controller`
4. Click Deploy

### Step 8: Share with Friend
Your friend needs to:
1. Download `PulseHost.exe` from your computer
2. Run it on their Windows machine
3. Enter:
   - Server URL: `wss://pulse-backend.onrender.com/ws`
   - Pairing code from your web dashboard
4. You open: `https://your-vercel-app.vercel.app`
5. Click Connect to control their computer

## ⚠️ Important Notes

1. **Free Tier Limitations:**
   - Render free tier spins down after 15 minutes of inactivity
   - First request after spin-down takes 30-60 seconds to wake up

2. **WebSocket Support:**
   - Render supports WebSockets on all plans
   - Use `wss://` (secure WebSocket) in production

3. **CORS:**
   - Backend already has CORS enabled
   - Should work with any frontend URL

## 🧪 Testing Deployment

After deployment, test with:
```bash
# Check if backend is running
curl https://pulse-backend.onrender.com/health

# Check WebSocket (use wscat)
npm install -g wscat
wscat -c wss://pulse-backend.onrender.com/ws
```

## 🔄 Auto-Deploy
Every time you push to GitHub main branch, Render will automatically redeploy!

## 💰 Costs
- Render Free Tier: $0/month (with limitations)
- Render Starter: $7/month (always on, no spin-down)
- Vercel Free: Unlimited for personal projects
