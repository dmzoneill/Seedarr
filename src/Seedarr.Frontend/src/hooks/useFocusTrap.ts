import { useEffect, useRef } from "react";

export const FOCUSABLE_SELECTOR =
  'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

export function isHtmlElement(el: any): el is HTMLElement {
  if (!el) return false;
  if (typeof HTMLElement !== "undefined") {
    return el instanceof HTMLElement;
  }
  return typeof el === "object" && typeof el.focus === "function";
}

export function isElementConnected(el: HTMLElement): boolean {
  if (typeof el.isConnected === "boolean") {
    return el.isConnected;
  }
  if (typeof document !== "undefined" && typeof document.contains === "function") {
    return document.contains(el);
  }
  return true;
}

export function isElementVisible(el: HTMLElement): boolean {
  if (el.hidden) return false;
  if (el.getAttribute?.("aria-hidden") === "true") return false;
  if (el.style?.display === "none" || el.style?.visibility === "hidden") return false;
  return true;
}

export function getFocusableElements(container: HTMLElement | null): HTMLElement[] {
  if (!container || typeof container.querySelectorAll !== "function") return [];
  const elements = Array.from(
    container.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)
  );
  return elements.filter(isElementVisible);
}

export interface FocusTrapOptions {
  container: HTMLElement | null;
  initialFocusElement?: HTMLElement | null;
  onEscape?: () => void;
  disableRestoreFocus?: boolean;
}

export class FocusTrap {
  private container: HTMLElement | null;
  private initialFocusElement?: HTMLElement | null;
  private onEscape?: () => void;
  private disableRestoreFocus: boolean;
  private previousActiveElement: HTMLElement | null = null;
  private isActive = false;
  private boundKeyDown: ((e: KeyboardEvent) => void) | null = null;

  constructor(options: FocusTrapOptions) {
    this.container = options.container;
    this.initialFocusElement = options.initialFocusElement;
    this.onEscape = options.onEscape;
    this.disableRestoreFocus = options.disableRestoreFocus ?? false;
  }

  public setContainer(container: HTMLElement | null): void {
    this.container = container;
  }

  public setInitialFocusElement(element?: HTMLElement | null): void {
    this.initialFocusElement = element;
  }

  public setOnEscape(onEscape?: () => void): void {
    this.onEscape = onEscape;
  }

  public setDisableRestoreFocus(disable: boolean): void {
    this.disableRestoreFocus = disable;
  }

  public getIsActive(): boolean {
    return this.isActive;
  }

  public getPreviousActiveElement(): HTMLElement | null {
    return this.previousActiveElement;
  }

  public activate(): void {
    if (this.isActive) return;
    this.isActive = true;

    if (typeof document !== "undefined" && isHtmlElement(document.activeElement)) {
      this.previousActiveElement = document.activeElement;
    }

    this.focusInitial();

    this.boundKeyDown = (e: KeyboardEvent) => this.handleKeyDown(e);
    if (typeof document !== "undefined") {
      document.addEventListener("keydown", this.boundKeyDown);
    }
  }

  public deactivate(): void {
    if (!this.isActive) return;
    this.isActive = false;

    if (this.boundKeyDown && typeof document !== "undefined") {
      document.removeEventListener("keydown", this.boundKeyDown);
      this.boundKeyDown = null;
    }

    if (!this.disableRestoreFocus && this.previousActiveElement) {
      const el = this.previousActiveElement;
      this.previousActiveElement = null;
      if (typeof el.focus === "function" && isElementConnected(el)) {
        try {
          el.focus();
        } catch {
          // Ignore if element is no longer focusable
        }
      }
    }
  }

  public focusInitial(): void {
    if (!this.container) return;

    if (this.initialFocusElement && isElementConnected(this.initialFocusElement)) {
      this.initialFocusElement.focus();
      return;
    }

    const focusables = getFocusableElements(this.container);
    if (focusables.length > 0) {
      focusables[0].focus();
    } else {
      if (!this.container.hasAttribute("tabindex")) {
        this.container.setAttribute("tabindex", "-1");
      }
      this.container.focus();
    }
  }

  public handleKeyDown(event: KeyboardEvent): void {
    if (!this.isActive || !this.container) return;
    if (event.defaultPrevented) return;

    if (event.key === "Escape") {
      if (this.onEscape) {
        event.preventDefault();
        this.onEscape();
      }
      return;
    }

    if (event.key === "Tab") {
      const focusables = getFocusableElements(this.container);

      if (focusables.length === 0) {
        event.preventDefault();
        if (!this.container.hasAttribute("tabindex")) {
          this.container.setAttribute("tabindex", "-1");
        }
        this.container.focus();
        return;
      }

      const first = focusables[0];
      const last = focusables[focusables.length - 1];
      const active = typeof document !== "undefined" ? document.activeElement : null;

      if (event.shiftKey) {
        if (active === first || !this.container.contains(active as Node)) {
          event.preventDefault();
          last.focus();
        }
      } else {
        if (active === last || !this.container.contains(active as Node)) {
          event.preventDefault();
          first.focus();
        }
      }
    }
  }
}

export interface UseFocusTrapOptions {
  isOpen?: boolean;
  initialFocusRef?: React.RefObject<HTMLElement | null>;
  onEscape?: () => void;
  onClose?: () => void;
  disableRestoreFocus?: boolean;
}

export function useFocusTrap<T extends HTMLElement = HTMLDivElement>(
  options?: UseFocusTrapOptions | boolean,
  onCloseCallback?: () => void,
): React.RefObject<T | null> {
  const normalizedOptions: UseFocusTrapOptions =
    typeof options === "boolean"
      ? { isOpen: options, onClose: onCloseCallback }
      : (options ?? {});

  const {
    isOpen = true,
    initialFocusRef,
    onEscape,
    onClose = onCloseCallback,
    disableRestoreFocus = false,
  } = normalizedOptions;

  const effectiveEscape = onEscape ?? onClose;

  const containerRef = useRef<T | null>(null);
  const trapRef = useRef<FocusTrap | null>(null);

  const onEscapeRef = useRef(effectiveEscape);
  onEscapeRef.current = effectiveEscape;
  const disableRestoreFocusRef = useRef(disableRestoreFocus);
  disableRestoreFocusRef.current = disableRestoreFocus;

  useEffect(() => {
    if (!isOpen) {
      if (trapRef.current) {
        trapRef.current.deactivate();
        trapRef.current = null;
      }
      return;
    }

    const trap = new FocusTrap({
      container: containerRef.current,
      initialFocusElement: initialFocusRef?.current,
      onEscape: () => onEscapeRef.current?.(),
      disableRestoreFocus: disableRestoreFocusRef.current,
    });

    trapRef.current = trap;
    trap.activate();

    let animId: number | null = null;
    let timerId: ReturnType<typeof setTimeout> | null = null;

    const delayedCheck = () => {
      if (!trap.getIsActive() || !containerRef.current) return;
      if (
        typeof document !== "undefined" &&
        (!document.activeElement || !containerRef.current.contains(document.activeElement))
      ) {
        trap.setContainer(containerRef.current);
        if (initialFocusRef?.current) {
          trap.setInitialFocusElement(initialFocusRef.current);
        }
        trap.focusInitial();
      }
    };

    if (typeof requestAnimationFrame === "function") {
      animId = requestAnimationFrame(delayedCheck);
    } else {
      timerId = setTimeout(delayedCheck, 0);
    }

    return () => {
      if (animId !== null && typeof cancelAnimationFrame === "function") {
        cancelAnimationFrame(animId);
      }
      if (timerId !== null) {
        clearTimeout(timerId);
      }
      trap.deactivate();
      if (trapRef.current === trap) {
        trapRef.current = null;
      }
    };
  }, [isOpen, initialFocusRef]);

  return containerRef;
}

export default useFocusTrap;
