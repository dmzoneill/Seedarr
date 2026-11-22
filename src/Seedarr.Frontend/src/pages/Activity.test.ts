import { describe, it } from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import Activity from "./Activity";

(globalThis as unknown as { React: typeof React }).React = React;

function seedQueryError(
  queryClient: QueryClient,
  queryKey: readonly unknown[],
  message: string,
) {
  const query = queryClient.getQueryCache().build(queryClient, {
    queryKey,
    queryFn: () => Promise.reject(new Error(message)),
  });
  query.setState({
    status: "error",
    fetchStatus: "idle",
    error: new Error(message),
    data: undefined,
  });
}

void describe("Activity torrent load errors", () => {
  void it("shows a torrent failure instead of a live zero peer count", () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false, retryOnMount: false },
      },
    });
    seedQueryError(queryClient, ["torrents"], "GET /torrent failed");

    const markup = renderToStaticMarkup(
      React.createElement(
        QueryClientProvider,
        { client: queryClient },
        React.createElement(Activity),
      ),
    );

    assert.ok(
      markup.includes("Failed to load torrents."),
      "Activity must surface useTorrents isError",
    );
    assert.ok(markup.includes('role="alert"'));
    assert.ok(
      markup.includes(">—<"),
      "Peer count must not render as a successful zero when torrents failed to load",
    );
  });

  void it("shows seeding stats and speed history failures instead of zero charts", () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false, retryOnMount: false },
      },
    });
    queryClient.setQueryData(["torrents"], []);
    seedQueryError(queryClient, ["seeding", "stats"], "stats failed");
    seedQueryError(queryClient, ["seeding", "history"], "history failed");

    const markup = renderToStaticMarkup(
      React.createElement(
        QueryClientProvider,
        { client: queryClient },
        React.createElement(Activity),
      ),
    );

    assert.ok(markup.includes("Failed to load seeding stats."));
    assert.ok(markup.includes("Failed to load speed history."));
    assert.equal(markup.includes("Failed to load torrents."), false);
    assert.ok(markup.includes(">—<"));
  });
});
