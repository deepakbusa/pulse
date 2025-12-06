import React, { useEffect, useRef, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { apiService } from '../api';
import './Session.css';

export const Session: React.FC = () => {
  const { sessionId } = useParams<{ sessionId: string }>();
  const navigate = useNavigate();
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const wsRef = useRef<WebSocket | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  const [frameCount, setFrameCount] = useState(0);
  const [quality, setQuality] = useState<'high' | 'medium' | 'low'>('high');
  const isRenderingRef = useRef(false); // Prevent frame backlog
  const latestFrameNumberRef = useRef(0); // Track latest frame from host
  const lastRenderedFrameRef = useRef(0); // Track what we rendered

  useEffect(() => {
    connectWebSocket();

    return () => {
      if (wsRef.current) {
        // End session before closing (only if connection is open)
        if (sessionId && wsRef.current.readyState === WebSocket.OPEN) {
          wsRef.current.send(JSON.stringify({
            type: 'endSession',
            sessionId
          }));
        }
        wsRef.current.close();
      }
    };
  }, [sessionId]);

  const connectWebSocket = () => {
    const ws = apiService.createWebSocket();
    wsRef.current = ws;

    ws.onopen = () => {
      console.log('Session WebSocket connected');
      // Send anonymous controller connection (no auth needed)
      ws.send(JSON.stringify({
        type: 'controllerConnect',
        anonymous: true
      }));
      
      // Join the existing session
      if (sessionId) {
        console.log('Joining session:', sessionId);
        ws.send(JSON.stringify({
          type: 'joinSession',
          sessionId
        }));
      }
      
      setIsConnected(true);
    };

    ws.onmessage = (event) => {
      try {
        const message = JSON.parse(event.data);
        console.log('Session received message:', message.type, message);
        
        switch (message.type) {
          case 'sessionJoined':
            console.log('Successfully joined session:', message.sessionId);
            break;
          case 'frame':
            if (message.sessionId === sessionId) {
              // CRITICAL FIX: Only render if this frame is newer than what we've seen
              // Skip old frames that are backed up in the queue
              latestFrameNumberRef.current = message.frameNumber;
              const frameDiff = message.frameNumber - lastRenderedFrameRef.current;
              
              if (frameDiff > 10) {
                // Huge gap detected - skip to latest frame immediately
                console.log(`Skipping frames ${lastRenderedFrameRef.current} to ${message.frameNumber} (gap: ${frameDiff})`);
                renderFrame(message.imageData, message.frameNumber);
              } else if (frameDiff > 0) {
                // Normal progression - render this frame
                renderFrame(message.imageData, message.frameNumber);
              }
              // Else: old frame, ignore it completely
              
              setFrameCount(message.frameNumber);
            }
            break;
          case 'sessionEnded':
            alert('Session ended');
            navigate('/dashboard');
            break;
          case 'error':
            console.error('Session error:', message.error);
            alert(`Error: ${message.error}`);
            break;
        }
      } catch (err) {
        console.error('Failed to parse WebSocket message:', err);
      }
    };

    ws.onerror = (error) => {
      console.error('Session WebSocket error:', error);
      setIsConnected(false);
    };

    ws.onclose = () => {
      console.log('Session WebSocket disconnected');
      setIsConnected(false);
    };
  };

  const renderFrame = (imageData: string, frameNumber: number) => {
    // Skip frame if already rendering (prevent backlog)
    if (isRenderingRef.current) return;
    
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    isRenderingRef.current = true;
    const img = new Image();
    img.onload = () => {
      // Set canvas size to match image dimensions (for coordinate mapping)
      canvas.width = img.width;
      canvas.height = img.height;
      
      // Draw the frame
      ctx.drawImage(img, 0, 0);
      lastRenderedFrameRef.current = frameNumber; // Update what we rendered
      isRenderingRef.current = false;
    };
    img.onerror = () => {
      isRenderingRef.current = false;
    };
    img.src = `data:image/jpeg;base64,${imageData}`;
  };

  const sendInputEvent = (inputType: string, data: any) => {
    if (wsRef.current && wsRef.current.readyState === WebSocket.OPEN && sessionId) {
      wsRef.current.send(JSON.stringify({
        type: 'input',
        sessionId,
        inputType,
        data
      }));
    }
  };

  const handleMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const rect = canvas.getBoundingClientRect();
    const xNorm = (e.clientX - rect.left) / rect.width;
    const yNorm = (e.clientY - rect.top) / rect.height;

    sendInputEvent('mouseMove', { xNorm, yNorm });
  };

  const handleMouseDown = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const rect = canvas.getBoundingClientRect();
    const xNorm = (e.clientX - rect.left) / rect.width;
    const yNorm = (e.clientY - rect.top) / rect.height;

    sendInputEvent('mouseDown', { 
      xNorm, 
      yNorm, 
      button: e.button 
    });
  };

  const handleMouseUp = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const rect = canvas.getBoundingClientRect();
    const xNorm = (e.clientX - rect.left) / rect.width;
    const yNorm = (e.clientY - rect.top) / rect.height;

    sendInputEvent('mouseUp', { 
      xNorm, 
      yNorm, 
      button: e.button 
    });
  };

  const handleWheel = (e: React.WheelEvent<HTMLCanvasElement>) => {
    e.preventDefault();
    sendInputEvent('mouseWheel', { 
      deltaX: e.deltaX,
      deltaY: e.deltaY 
    });
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    e.preventDefault();
    sendInputEvent('keyDown', { 
      key: e.key,
      code: e.code,
      keyCode: e.keyCode,
      ctrlKey: e.ctrlKey,
      shiftKey: e.shiftKey,
      altKey: e.altKey
    });
  };

  const handleKeyUp = (e: React.KeyboardEvent) => {
    e.preventDefault();
    sendInputEvent('keyUp', { 
      key: e.key,
      code: e.code,
      keyCode: e.keyCode
    });
  };

  const handleEndSession = () => {
    if (wsRef.current && sessionId) {
      wsRef.current.send(JSON.stringify({
        type: 'endSession',
        sessionId
      }));
    }
    navigate('/dashboard');
  };

  return (
    <div className="session-container">
      <div className="session-toolbar">
        <div className="toolbar-left">
          <h3>Remote Session</h3>
          <span className={`connection-status ${isConnected ? 'connected' : 'disconnected'}`}>
            {isConnected ? '🟢 Connected' : '🔴 Disconnected'}
          </span>
          <span className="frame-counter">Frame: {frameCount}</span>
        </div>
        
        <div className="toolbar-right">
          <select 
            value={quality} 
            onChange={(e) => setQuality(e.target.value as any)}
            className="quality-select"
          >
            <option value="high">High Quality</option>
            <option value="medium">Medium Quality</option>
            <option value="low">Low Quality</option>
          </select>
          
          <button onClick={handleEndSession} className="btn btn-danger">
            End Session
          </button>
        </div>
      </div>

      <div className="session-viewport">
        <canvas
          ref={canvasRef}
          className="session-canvas"
          onMouseMove={handleMouseMove}
          onMouseDown={handleMouseDown}
          onMouseUp={handleMouseUp}
          onWheel={handleWheel}
          onKeyDown={handleKeyDown}
          onKeyUp={handleKeyUp}
          tabIndex={0}
        />
        
        {!isConnected && (
          <div className="connection-overlay">
            <div className="connection-message">
              Waiting for connection...
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
