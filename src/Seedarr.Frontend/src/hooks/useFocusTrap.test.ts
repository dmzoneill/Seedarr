import { describe, it, beforeEach, afterEach } from "node:test";
import assert from "node:assert/strict";
import {
  FocusTrap,
  getFocusableElements,
  isElementVisible,
  isElementConnected,
} from "./useFocusTrap";

class MockElement {
  public tagName: string;
  public id: string;
  public tabIndex: number = 0;
  public disabled: boolean = false;
  public hidden: boolean = false;
  public isConnected: boolean = true;
  public style: Record<string, string> = {};
  public attributes: Map<string, string> = new Map();
  public children: MockElement[] = [];
  public parent: MockElement | null = null;
  public focusCalls: number = 0;

  constructor(tagName: string, id: string = "") {
    this.tagName = tagName.toUpperCase();
    this.id = id;
  }

  setAttribute(name: string, value: string): void {
    this.attributes.set(name, value);
    if (name === "tabindex") {
      this.tabIndex = parseInt(value, 10);
    }
  }

  getAttribute(name: string): string | null {
    return this.attributes.get(name) ?? null;
  }

  hasAttribute(name: string): boolean {
    return this.attributes.has(name);
  }

  removeAttribute(name: string): void {
    this.attributes.delete(name);
  }

  appendChild(child: MockElement): void {
    child.parent = this;
    this.children.push(child);
  }

  contains(other: any): boolean {
    if (!other) return false;
    if (other === this) return true;
    for (const child of this.children) {
      if (child.contains(other)) return true;
    }
    return false;
  }

  focus(): void {
    this.focusCalls++;
    if (typeof (globalThis as any).document !== "undefined") {
      (globalThis as any).document.activeElement = this;
    }
  }

  blur(): void {
    if (typeof (globalThis as any).document !== "undefined") {
      if ((globalThis as any).document.activeElement === this) {
        (globalThis as any).document.activeElement = (
          globalThis as any
        ).document.body;
      }
    }
  }

  querySelectorAll<T = HTMLElement>(selector: string): T[] {
    const results: MockElement[] = [];
    const traverse = (el: MockElement) => {
      for (const child of el.children) {
        if (child.matches(selector)) {
          results.push(child);
        }
        traverse(child);
      }
    };
    traverse(this);
    return results as unknown as T[];
  }

  matches(selector: string): boolean {
    const parts = selector.split(",").map((s) => s.trim());
    return parts.some((p) => this.matchesSingle(p));
  }

  private matchesSingle(sel: string): boolean {
    if (sel.startsWith("button:not([disabled])")) {
      return this.tagName === "BUTTON" && !this.disabled;
    }
    if (sel.startsWith("input:not([disabled])")) {
      return this.tagName === "INPUT" && !this.disabled;
    }
    if (sel.startsWith("select:not([disabled])")) {
      return this.tagName === "SELECT" && !this.disabled;
    }
    if (sel.startsWith("textarea:not([disabled])")) {
      return this.tagName === "TEXTAREA" && !this.disabled;
    }
    if (sel.startsWith("[href]")) {
      return this.hasAttribute("href");
    }
    if (sel.startsWith('[tabindex]:not([tabindex="-1"])')) {
      return this.hasAttribute("tabindex") && this.tabIndex >= 0;
    }
    return false;
  }
}

class MockKeyboardEvent {
  public key: string;
  public shiftKey: boolean;
  public defaultPrevented: boolean = false;

  constructor(key: string, options: { shiftKey?: boolean } = {}) {
    this.key = key;
    this.shiftKey = options.shiftKey ?? false;
  }

  preventDefault(): void {
    this.defaultPrevented = true;
  }
}

describe("FocusTrap / useFocusTrap", () => {
  let originalDocument: any;
  let mockDocument: any;
  let documentListeners: Map<string, Set<(e: any) => void>>;
  let rootBody: MockElement;

  beforeEach(() => {
    originalDocument = (globalThis as any).document;
    documentListeners = new Map();
    rootBody = new MockElement("body", "body");

    mockDocument = {
      body: rootBody,
      activeElement: rootBody,
      contains: (el: any) => rootBody.contains(el),
      addEventListener: (type: string, listener: (e: any) => void) => {
        if (!documentListeners.has(type)) {
          documentListeners.set(type, new Set());
        }
        documentListeners.get(type)!.add(listener);
      },
      removeEventListener: (type: string, listener: (e: any) => void) => {
        documentListeners.get(type)?.delete(listener);
      },
      dispatchEvent: (event: any) => {
        const listeners = documentListeners.get("keydown") || [];
        for (const listener of listeners) {
          listener(event);
        }
      },
    };

    (globalThis as any).document = mockDocument;
  });

  afterEach(() => {
    (globalThis as any).document = originalDocument;
  });

  describe("Focusable Element Queries & Visibility", () => {
    it("identifies standard focusable selectors and filters disabled/hidden elements", () => {
      const container = new MockElement("div", "container");
      const btn1 = new MockElement("button", "btn1");
      const disabledBtn = new MockElement("button", "disabledBtn");
      disabledBtn.disabled = true;
      const link = new MockElement("a", "link");
      link.setAttribute("href", "#");
      const hiddenInput = new MockElement("input", "hiddenInput");
      hiddenInput.hidden = true;
      const visibleInput = new MockElement("input", "visibleInput");
      const negativeTabDiv = new MockElement("div", "negativeTabDiv");
      negativeTabDiv.setAttribute("tabindex", "-1");
      const positiveTabDiv = new MockElement("div", "positiveTabDiv");
      positiveTabDiv.setAttribute("tabindex", "0");

      container.appendChild(btn1);
      container.appendChild(disabledBtn);
      container.appendChild(link);
      container.appendChild(hiddenInput);
      container.appendChild(visibleInput);
      container.appendChild(negativeTabDiv);
      container.appendChild(positiveTabDiv);

      const focusables = getFocusableElements(
        container as unknown as HTMLElement,
      );
      const ids = focusables.map((el) => (el as unknown as MockElement).id);

      assert.deepEqual(ids, ["btn1", "link", "visibleInput", "positiveTabDiv"]);
    });

    it("evaluates isElementVisible and isElementConnected helpers", () => {
      const el = new MockElement("div");
      assert.equal(isElementVisible(el as unknown as HTMLElement), true);

      el.hidden = true;
      assert.equal(isElementVisible(el as unknown as HTMLElement), false);

      el.hidden = false;
      el.setAttribute("aria-hidden", "true");
      assert.equal(isElementVisible(el as unknown as HTMLElement), false);

      el.removeAttribute("aria-hidden");
      el.style = { display: "none" };
      assert.equal(isElementVisible(el as unknown as HTMLElement), false);

      el.style = { visibility: "hidden" };
      assert.equal(isElementVisible(el as unknown as HTMLElement), false);

      el.style = {};
      assert.equal(isElementVisible(el as unknown as HTMLElement), true);
      assert.equal(isElementConnected(el as unknown as HTMLElement), true);

      el.isConnected = false;
      assert.equal(isElementConnected(el as unknown as HTMLElement), false);
    });
  });

  describe("Initial Focus Placement", () => {
    it("focuses initialFocusElement when specified and connected", () => {
      const container = new MockElement("div", "container");
      const btn1 = new MockElement("button", "btn1");
      const input2 = new MockElement("input", "input2");
      container.appendChild(btn1);
      container.appendChild(input2);
      rootBody.appendChild(container);

      const trap = new FocusTrap({
        container: container as unknown as HTMLElement,
        initialFocusElement: input2 as unknown as HTMLElement,
      });

      trap.activate();
      assert.equal(mockDocument.activeElement, input2);
      assert.equal(input2.focusCalls, 1);
      assert.equal(btn1.focusCalls, 0);

      trap.deactivate();
    });

    it("focuses first focusable element when initialFocusElement is not specified", () => {
      const container = new MockElement("div", "container");
      const btn1 = new MockElement("button", "btn1");
      const btn2 = new MockElement("button", "btn2");
      container.appendChild(btn1);
      container.appendChild(btn2);
      rootBody.appendChild(container);

      const trap = new FocusTrap({
        container: container as unknown as HTMLElement,
      });

      trap.activate();
      assert.equal(mockDocument.activeElement, btn1);
      assert.equal(btn1.focusCalls, 1);
      assert.equal(btn2.focusCalls, 0);

      trap.deactivate();
    });

    it("focuses container itself and adds tabindex=-1 when no focusable elements exist", () => {
      const container = new MockElement("div", "container");
      const text = new MockElement("span", "text");
      container.appendChild(text);
      rootBody.appendChild(container);

      const trap = new FocusTrap({
        container: container as unknown as HTMLElement,
      });

      trap.activate();
      assert.equal(container.getAttribute("tabindex"), "-1");
      assert.equal(mockDocument.activeElement, container);
      assert.equal(container.focusCalls, 1);

      trap.deactivate();
    });
  });

  describe("Focus Restoration", () => {
    it("restores focus to previousActiveElement upon deactivation", () => {
      const triggerBtn = new MockElement("button", "triggerBtn");
      rootBody.appendChild(triggerBtn);
      triggerBtn.focus();
      assert.equal(mockDocument.activeElement, triggerBtn);

      const container = new MockElement("div", "container");
      const closeBtn = new MockElement("button", "closeBtn");
      container.appendChild(closeBtn);
      rootBody.appendChild(container);

      const trap = new FocusTrap({
        container: container as unknown as HTMLElement,
      });

      trap.activate();
      assert.equal(mockDocument.activeElement, closeBtn);
      assert.equal(trap.getPreviousActiveElement(), triggerBtn);

      trap.deactivate();
      assert.equal(mockDocument.activeElement, triggerBtn);
      assert.equal(triggerBtn.focusCalls, 2); // Initial focus + restored focus
    });

    it("does not restore focus if disableRestoreFocus is true", () => {
      const triggerBtn = new MockElement("button", "triggerBtn");
      rootBody.appendChild(triggerBtn);
      triggerBtn.focus();

      const container = new MockElement("div", "container");
      const closeBtn = new MockElement("button", "closeBtn");
      container.appendChild(closeBtn);
      rootBody.appendChild(container);

      const trap = new FocusTrap({
        container: container as unknown as HTMLElement,
        disableRestoreFocus: true,
      });

      trap.activate();
      assert.equal(mockDocument.activeElement, closeBtn);

      trap.deactivate();
      assert.equal(mockDocument.activeElement, closeBtn); // Kept on closeBtn
      assert.equal(triggerBtn.focusCalls, 1);
    });

    it("skips focus restoration if previousActiveElement is disconnected", () => {
      const triggerBtn = new MockElement("button", "triggerBtn");
      rootBody.appendChild(triggerBtn);
      triggerBtn.focus();

      const container = new MockElement("div", "container");
      const closeBtn = new MockElement("button", "closeBtn");
      container.appendChild(closeBtn);
      rootBody.appendChild(container);

      const trap = new FocusTrap({
        container: container as unknown as HTMLElement,
      });

      trap.activate();
      triggerBtn.isConnected = false; // Detached while modal was open

      trap.deactivate();
      assert.equal(triggerBtn.focusCalls, 1); // Not called again
    });
  });

  describe("Keyboard Focus Trap Cycle & Escape Handling", () => {
    it("wraps around from last to first element on Tab keydown", () => {
      const container = new MockElement("div", "container");
      const btn1 = new MockElement("button", "btn1");
      const btn2 = new MockElement("button", "btn2");
      const btn3 = new MockElement("button", "btn3");
      container.appendChild(btn1);
      container.appendChild(btn2);
      container.appendChild(btn3);
      rootBody.appendChild(container);

      const trap = new FocusTrap({
        container: container as unknown as HTMLElement,
      });
      trap.activate();

      // Focus on last element
      btn3.focus();
      assert.equal(mockDocument.activeElement, btn3);

      const tabEvent = new MockKeyboardEvent("Tab", { shiftKey: false });
      trap.handleKeyDown(tabEvent as unknown as KeyboardEvent);

      assert.equal(tabEvent.defaultPrevented, true);
      assert.equal(mockDocument.activeElement, btn1);

      trap.deactivate();
    });

    it("wraps around from first to last element on Shift+Tab keydown", () => {
      const container = new MockElement("div", "container");
      const btn1 = new MockElement("button", "btn1");
      const btn2 = new MockElement("button", "btn2");
      const btn3 = new MockElement("button", "btn3");
      container.appendChild(btn1);
      container.appendChild(btn2);
      container.appendChild(btn3);
      rootBody.appendChild(container);

      const trap = new FocusTrap({
        container: container as unknown as HTMLElement,
      });
      trap.activate();

      // Focus on first element
      btn1.focus();
      assert.equal(mockDocument.activeElement, btn1);

      const shiftTabEvent = new MockKeyboardEvent("Tab", { shiftKey: true });
      trap.handleKeyDown(shiftTabEvent as unknown as KeyboardEvent);

      assert.equal(shiftTabEvent.defaultPrevented, true);
      assert.equal(mockDocument.activeElement, btn3);

      trap.deactivate();
    });

    it("reclaims focus into modal if activeElement is outside container on Tab", () => {
      const outsideBtn = new MockElement("button", "outsideBtn");
      rootBody.appendChild(outsideBtn);

      const container = new MockElement("div", "container");
      const btn1 = new MockElement("button", "btn1");
      const btn2 = new MockElement("button", "btn2");
      container.appendChild(btn1);
      container.appendChild(btn2);
      rootBody.appendChild(container);

      const trap = new FocusTrap({
        container: container as unknown as HTMLElement,
      });
      trap.activate();

      // Outside focus
      outsideBtn.focus();
      assert.equal(mockDocument.activeElement, outsideBtn);

      // Normal Tab pulls to first
      const tabEvent = new MockKeyboardEvent("Tab", { shiftKey: false });
      trap.handleKeyDown(tabEvent as unknown as KeyboardEvent);
      assert.equal(tabEvent.defaultPrevented, true);
      assert.equal(mockDocument.activeElement, btn1);

      // Outside focus again
      outsideBtn.focus();

      // Shift+Tab pulls to last
      const shiftTabEvent = new MockKeyboardEvent("Tab", { shiftKey: true });
      trap.handleKeyDown(shiftTabEvent as unknown as KeyboardEvent);
      assert.equal(shiftTabEvent.defaultPrevented, true);
      assert.equal(mockDocument.activeElement, btn2);

      trap.deactivate();
    });

    it("allows default browser tab behavior when navigating between intermediate elements", () => {
      const container = new MockElement("div", "container");
      const btn1 = new MockElement("button", "btn1");
      const btn2 = new MockElement("button", "btn2");
      const btn3 = new MockElement("button", "btn3");
      container.appendChild(btn1);
      container.appendChild(btn2);
      container.appendChild(btn3);
      rootBody.appendChild(container);

      const trap = new FocusTrap({
        container: container as unknown as HTMLElement,
      });
      trap.activate();

      btn2.focus();
      const tabEvent = new MockKeyboardEvent("Tab", { shiftKey: false });
      trap.handleKeyDown(tabEvent as unknown as KeyboardEvent);
      // Not at boundary: browser default Tab moves focus naturally
      assert.equal(tabEvent.defaultPrevented, false);

      const shiftTabEvent = new MockKeyboardEvent("Tab", { shiftKey: true });
      trap.handleKeyDown(shiftTabEvent as unknown as KeyboardEvent);
      // Not at boundary: browser default Shift+Tab moves focus naturally
      assert.equal(shiftTabEvent.defaultPrevented, false);

      trap.deactivate();
    });

    it("invokes onEscape and prevents default when Escape is pressed", () => {
      const container = new MockElement("div", "container");
      const btn1 = new MockElement("button", "btn1");
      container.appendChild(btn1);
      rootBody.appendChild(container);

      let escapeCalled = false;
      const trap = new FocusTrap({
        container: container as unknown as HTMLElement,
        onEscape: () => {
          escapeCalled = true;
        },
      });
      trap.activate();

      const escapeEvent = new MockKeyboardEvent("Escape");
      trap.handleKeyDown(escapeEvent as unknown as KeyboardEvent);

      assert.equal(escapeCalled, true);
      assert.equal(escapeEvent.defaultPrevented, true);

      trap.deactivate();
    });

    it("handles keydown dispatched through mockDocument listener", () => {
      const container = new MockElement("div", "container");
      const btn1 = new MockElement("button", "btn1");
      const btn2 = new MockElement("button", "btn2");
      container.appendChild(btn1);
      container.appendChild(btn2);
      rootBody.appendChild(container);

      let escapeCalled = false;
      const trap = new FocusTrap({
        container: container as unknown as HTMLElement,
        onEscape: () => {
          escapeCalled = true;
        },
      });
      trap.activate();

      const escapeEvent = new MockKeyboardEvent("Escape");
      mockDocument.dispatchEvent(escapeEvent);

      assert.equal(escapeCalled, true);
      assert.equal(escapeEvent.defaultPrevented, true);

      trap.deactivate();
      escapeCalled = false;

      // After deactivation, listener is removed
      const secondEscape = new MockKeyboardEvent("Escape");
      mockDocument.dispatchEvent(secondEscape);
      assert.equal(escapeCalled, false);
    });
  });
});
