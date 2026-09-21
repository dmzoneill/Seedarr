import {
  createContext,
  useContext,
  useState,
  useCallback,
  useEffect,
} from "react";
import { useGeneralConfig } from "../api/hooks";
import { trackThemeChange } from "../utils/analytics";

export type Theme = "dark" | "light" | "indigo" | "oled" | "slate" | "system";
export type Accent =
  | "auto"
  | "blue"
  | "emerald"
  | "purple"
  | "rose"
  | "cyan"
  | "amber";

export interface ThemeContextValue {
  theme: Theme;
  setTheme: (theme: Theme) => void;
  accent: Accent;
  setAccent: (accent: Accent) => void;
  toggleTheme: () => void;
}

const STORAGE_THEME_KEY = "seedarr-theme";
const STORAGE_ACCENT_KEY = "seedarr-accent";

const ThemeContext = createContext<ThemeContextValue | null>(null);

const VALID_THEMES: Theme[] = [
  "dark",
  "light",
  "indigo",
  "oled",
  "slate",
  "system",
];
const VALID_ACCENTS: Accent[] = [
  "auto",
  "blue",
  "emerald",
  "purple",
  "rose",
  "cyan",
  "amber",
];

function getInitialTheme(): Theme {
  try {
    const stored = localStorage.getItem(STORAGE_THEME_KEY) as Theme | null;
    if (stored && VALID_THEMES.includes(stored)) {
      return stored;
    }
  } catch {
    // localStorage may be unavailable
  }
  return "dark";
}

function getInitialAccent(): Accent {
  try {
    const stored = localStorage.getItem(STORAGE_ACCENT_KEY);
    if (stored === "green") {
      return "emerald";
    }
    if (stored && VALID_ACCENTS.includes(stored as Accent)) {
      return stored as Accent;
    }
  } catch {
    // localStorage may be unavailable
  }
  return "auto";
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(getInitialTheme);
  const [accent, setAccentState] = useState<Accent>(getInitialAccent);
  const { data: config } = useGeneralConfig();

  useEffect(() => {
    if (config) {
      const serverTheme = (config.uiTheme || config.themeStyle) as Theme;
      let rawAccent = config.uiAccent || config.colorScheme;
      if (rawAccent === "green") {
        rawAccent = "emerald";
      }
      const serverAccent = rawAccent as Accent;

      try {
        const storedTheme = localStorage.getItem(STORAGE_THEME_KEY);
        if (!storedTheme && serverTheme && VALID_THEMES.includes(serverTheme)) {
          setThemeState(serverTheme);
        }
      } catch {
        if (serverTheme && VALID_THEMES.includes(serverTheme)) {
          setThemeState(serverTheme);
        }
      }

      try {
        const storedAccent = localStorage.getItem(STORAGE_ACCENT_KEY);
        if (!storedAccent && serverAccent && VALID_ACCENTS.includes(serverAccent)) {
          setAccentState(serverAccent);
        }
      } catch {
        if (serverAccent && VALID_ACCENTS.includes(serverAccent)) {
          setAccentState(serverAccent);
        }
      }
    }
  }, [config]);

  const setTheme = useCallback((newTheme: Theme) => {
    trackThemeChange(newTheme);
    setThemeState(newTheme);
    try {
      localStorage.setItem(STORAGE_THEME_KEY, newTheme);
    } catch {
      // localStorage may be unavailable
    }
  }, []);

  const setAccent = useCallback((newAccent: Accent) => {
    setAccentState(newAccent);
    try {
      localStorage.setItem(STORAGE_ACCENT_KEY, newAccent);
    } catch {
      // localStorage may be unavailable
    }
  }, []);

  useEffect(() => {
    const applyTheme = () => {
      let resolvedTheme = theme;
      if (theme === "system") {
        resolvedTheme = window.matchMedia("(prefers-color-scheme: light)")
          .matches
          ? "light"
          : "dark";
      }
      document.documentElement.setAttribute("data-theme", resolvedTheme);
      document.documentElement.setAttribute("data-accent", accent);
    };

    applyTheme();

    if (theme === "system") {
      const mediaQuery = window.matchMedia("(prefers-color-scheme: light)");
      const handler = () => applyTheme();
      mediaQuery.addEventListener("change", handler);
      return () => mediaQuery.removeEventListener("change", handler);
    }
  }, [theme, accent]);

  const toggleTheme = useCallback(() => {
    let resolvedTheme: "light" | "dark";
    if (theme === "system") {
      resolvedTheme = window.matchMedia("(prefers-color-scheme: light)").matches
        ? "light"
        : "dark";
    } else if (theme === "light" || theme === "dark") {
      resolvedTheme = theme;
    } else {
      resolvedTheme = "dark";
    }

    const nextTheme: Theme = resolvedTheme === "dark" ? "light" : "dark";
    setThemeState(nextTheme);
    try {
      localStorage.setItem(STORAGE_THEME_KEY, nextTheme);
    } catch {
      // localStorage may be unavailable
    }
  }, [theme]);

  return (
    <ThemeContext.Provider
      value={{ theme, setTheme, accent, setAccent, toggleTheme }}
    >
      {children}
    </ThemeContext.Provider>
  );
}

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext);
  if (!ctx) {
    throw new Error("useTheme must be used within a ThemeProvider");
  }
  return ctx;
}

export default ThemeContext;
