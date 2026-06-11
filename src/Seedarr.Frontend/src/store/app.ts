import { create } from 'zustand';

interface AppState {
  apiKey: string;
  isConnected: boolean;
  setApiKey: (key: string) => void;
  setConnected: (connected: boolean) => void;
}

export const useAppStore = create<AppState>((set) => ({
  apiKey: '',
  isConnected: false,
  setApiKey: (key: string) => set({ apiKey: key }),
  setConnected: (connected: boolean) => set({ isConnected: connected }),
}));
