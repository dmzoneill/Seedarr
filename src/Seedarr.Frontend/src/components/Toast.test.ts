import { describe, it } from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import ToastContainer from "./Toast";
import ToastContext, { type ToastType } from "../context/ToastContext";

(globalThis as any).React = React;

describe("ToastContainer accessibility and rendering", () => {
  it("renders nothing when context is null or toasts array is empty", () => {
    const htmlEmpty = renderToStaticMarkup(
      React.createElement(
        ToastContext.Provider,
        {
          value: {
            toasts: [],
            showToast: () => {},
            removeToast: () => {},
          },
        },
        React.createElement(ToastContainer),
      ),
    );
    assert.equal(htmlEmpty, "");

    const htmlNull = renderToStaticMarkup(
      React.createElement(
        ToastContext.Provider,
        {
          value: null as any,
        },
        React.createElement(ToastContainer),
      ),
    );
    assert.equal(htmlNull, "");
  });

  it("renders container with role region and aria-label Notifications", () => {
    const html = renderToStaticMarkup(
      React.createElement(
        ToastContext.Provider,
        {
          value: {
            toasts: [
              {
                id: 1,
                message: "Torrent added successfully",
                type: "success" as ToastType,
              },
            ],
            showToast: () => {},
            removeToast: () => {},
          },
        },
        React.createElement(ToastContainer),
      ),
    );

    assert.ok(
      html.includes("toast-container"),
      "Should have toast-container class",
    );
    assert.ok(
      html.includes('role="region"'),
      "Container should have role region",
    );
    assert.ok(
      html.includes('aria-label="Notifications"'),
      "Container should have aria-label Notifications",
    );
  });

  it("renders polite status role for non-error toasts (success, info, warning)", () => {
    const toasts = [
      { id: 1, message: "Torrent resumed", type: "success" as ToastType },
      { id: 2, message: "Connecting to peers", type: "info" as ToastType },
      { id: 3, message: "Disk space low", type: "warning" as ToastType },
    ];

    const html = renderToStaticMarkup(
      React.createElement(
        ToastContext.Provider,
        {
          value: {
            toasts,
            showToast: () => {},
            removeToast: () => {},
          },
        },
        React.createElement(ToastContainer),
      ),
    );

    const statusMatches = html.match(/role="status"/g);
    assert.equal(
      statusMatches?.length,
      3,
      "All 3 non-error toasts should have role status",
    );

    const liveMatches = html.match(/aria-live="polite"/g);
    assert.equal(
      liveMatches?.length,
      3,
      "All 3 non-error toasts should have aria-live polite",
    );

    const atomicMatches = html.match(/aria-atomic="true"/g);
    assert.equal(
      atomicMatches?.length,
      3,
      "All 3 non-error toasts should have aria-atomic true",
    );

    assert.ok(html.includes("Torrent resumed"));
    assert.ok(html.includes("Connecting to peers"));
    assert.ok(html.includes("Disk space low"));
  });

  it("renders assertive alert role for error toasts", () => {
    const toasts = [
      {
        id: 1,
        message: "Failed to connect to tracker",
        type: "error" as ToastType,
      },
    ];

    const html = renderToStaticMarkup(
      React.createElement(
        ToastContext.Provider,
        {
          value: {
            toasts,
            showToast: () => {},
            removeToast: () => {},
          },
        },
        React.createElement(ToastContainer),
      ),
    );

    assert.ok(
      html.includes('role="alert"'),
      "Error toast should have role alert",
    );
    assert.ok(
      html.includes('aria-live="assertive"'),
      "Error toast should have aria-live assertive",
    );
    assert.ok(
      html.includes('aria-atomic="true"'),
      "Error toast should have aria-atomic true",
    );
    assert.ok(html.includes("Failed to connect to tracker"));
    assert.ok(html.includes('aria-label="Dismiss"'));
  });
});
