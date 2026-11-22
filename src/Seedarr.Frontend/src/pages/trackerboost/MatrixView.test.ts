import { describe, it } from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MatrixView } from "./MatrixView";

(globalThis as unknown as { React: typeof React }).React = React;

function queryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, retryOnMount: false },
    },
  });
}

function seedQueryError(
  client: QueryClient,
  queryKey: readonly unknown[],
  message: string,
) {
  const query = client.getQueryCache().build(client, {
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

function renderMatrix(client: QueryClient) {
  return renderToStaticMarkup(
    React.createElement(
      QueryClientProvider,
      { client },
      React.createElement(MatrixView, { torrentMetaMap: new Map() }),
    ),
  );
}

void describe("MatrixView load errors", () => {
  void it("shows a matrix failure instead of an empty search result", () => {
    const client = queryClient();
    seedQueryError(client, ["trackerboost", "matrix"], "matrix failed");
    client.setQueryData(["torrents"], []);

    const html = renderMatrix(client);

    assert.ok(html.includes("Failed to load data."));
    assert.equal(html.includes("No torrents match the search query."), false);
    assert.equal(
      html.includes("No tracker endpoints match the search query."),
      false,
    );
  });

  void it("shows a torrent failure when library matching cannot load", () => {
    const client = queryClient();
    client.setQueryData(["trackerboost", "matrix"], {
      torrents: [],
      trackers: [],
    });
    seedQueryError(client, ["torrents"], "GET /torrent failed");

    const html = renderMatrix(client);

    assert.ok(html.includes("Failed to load torrents."));
    assert.ok(html.includes('role="alert"'));
  });
});
