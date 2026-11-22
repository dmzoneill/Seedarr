import { describe, it } from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import TorrentGrid from "./TorrentGrid";

(globalThis as unknown as { React: typeof React }).React = React;

void describe("TorrentGrid load errors", () => {
  void it("shows a failure state instead of an empty torrent list", () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false, retryOnMount: false },
      },
    });
    const query = queryClient.getQueryCache().build(queryClient, {
      queryKey: ["torrents"],
      queryFn: () => Promise.reject(new Error("GET /torrent failed")),
    });
    query.setState({
      status: "error",
      fetchStatus: "idle",
      error: new Error("GET /torrent failed"),
      data: undefined,
    });

    const html = renderToStaticMarkup(
      React.createElement(
        QueryClientProvider,
        { client: queryClient },
        React.createElement(TorrentGrid, {}),
      ),
    );

    assert.ok(html.includes("Failed to load data."));
    assert.equal(html.includes("No torrents found"), false);
  });
});
