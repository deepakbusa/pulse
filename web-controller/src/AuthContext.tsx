import React, { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { apiService, type User } from './api';

interface AuthContextType {
  user: User | null;
  token: string | null;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string) => Promise<void>;
  logout: () => void;
  isLoading: boolean;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<User | null>(null);
  const [token, setToken] = useState<string | null>(apiService.getToken());
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const loadUser = async () => {
      if (token) {
        try {
          const currentUser = await apiService.getCurrentUser();
          setUser(currentUser);
        } catch (error) {
          console.error('Failed to load user, clearing invalid token:', error);
          // Clear invalid token and force re-login
          apiService.clearToken();
          setToken(null);
          setUser(null);
          // Force reload to clear any cached state
          window.location.reload();
        }
      }
      setIsLoading(false);
    };

    loadUser();
  }, [token]);

  const login = async (email: string, password: string) => {
    // Clear any old token first
    apiService.clearToken();
    const response = await apiService.login(email, password);
    apiService.setToken(response.token);
    setToken(response.token);
    setUser(response.user);
    console.log('Login successful, new token set');
  };

  const register = async (email: string, password: string) => {
    const response = await apiService.register(email, password);
    apiService.setToken(response.token);
    setToken(response.token);
    setUser(response.user);
  };

  const logout = () => {
    apiService.clearToken();
    setToken(null);
    setUser(null);
  };

  return (
    <AuthContext.Provider value={{ user, token, login, register, logout, isLoading }}>
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = () => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within AuthProvider');
  }
  return context;
};
