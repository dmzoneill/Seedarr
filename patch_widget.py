import sys

with open('src/Seedarr.Frontend/src/pages/AutomationPage.tsx', 'r') as f:
    content = f.read()

http_widget = """                                ) : act.type === "http" ? (
                                  <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "0.5rem" }}>
                                    <div style={{ display: "flex", gap: "0.5rem" }}>
                                      <select
                                        className="form-control"
                                        style={{ width: "100px", color: act.extra?.method === "GET" ? "#3b82f6" : act.extra?.method === "POST" ? "#10b981" : act.extra?.method === "PUT" ? "#f59e0b" : act.extra?.method === "DELETE" ? "#ef4444" : "#8b5cf6", fontWeight: "bold" }}
                                        value={act.extra?.method || "POST"}
                                        onChange={(e) => {
                                          const copy = [...visualSteps];
                                          if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                          copy[stepIdx].actions[actIdx].extra!.method = e.target.value;
                                          updateVisualSteps(copy);
                                        }}
                                      >
                                        <option value="GET" style={{ color: "#3b82f6" }}>GET</option>
                                        <option value="POST" style={{ color: "#10b981" }}>POST</option>
                                        <option value="PUT" style={{ color: "#f59e0b" }}>PUT</option>
                                        <option value="DELETE" style={{ color: "#ef4444" }}>DELETE</option>
                                        <option value="PATCH" style={{ color: "#8b5cf6" }}>PATCH</option>
                                      </select>
                                      <input
                                        type="text"
                                        className="form-control"
                                        style={{ flex: 1 }}
                                        value={act.extra?.url || act.value || ""}
                                        placeholder="https://external.service.com/api/v1/webhook"
                                        onChange={(e) => {
                                          const copy = [...visualSteps];
                                          copy[stepIdx].actions[actIdx].value = e.target.value;
                                          if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                          copy[stepIdx].actions[actIdx].extra!.url = e.target.value;
                                          updateVisualSteps(copy);
                                        }}
                                      />
                                    </div>
                                    <textarea
                                      className="form-control"
                                      style={{ minHeight: "80px", fontFamily: "monospace", fontSize: "0.85rem" }}
                                      placeholder={`{"event": "complete", "torrent": "${torrent.name}", "size": ${torrent.size}}`}
                                      value={act.extra?.body || ""}
                                      onChange={(e) => {
                                        const copy = [...visualSteps];
                                        if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                        copy[stepIdx].actions[actIdx].extra!.body = e.target.value;
                                        updateVisualSteps(copy);
                                      }}
                                    />
                                    <div style={{ display: "flex", gap: "1rem", fontSize: "0.85rem", alignItems: "center", flexWrap: "wrap" }}>
                                      <button
                                        type="button"
                                        className="btn btn-sm btn-outline"
                                        title="✨ Insert Torrent JSON Payload"
                                        onClick={(e) => {
                                          e.preventDefault();
                                          const copy = [...visualSteps];
                                          if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                          copy[stepIdx].actions[actIdx].extra!.body = '{\\n  "event": "complete",\\n  "torrent": "${torrent.name}",\\n  "size": ${torrent.size},\\n  "hash": "${torrent.infoHash}"\\n}';
                                          updateVisualSteps(copy);
                                        }}
                                      >
                                        ✨ Template
                                      </button>
                                      <select
                                        className="form-control"
                                        style={{ width: "130px", fontSize: "0.8rem", padding: "0.2rem 0.5rem" }}
                                        value=""
                                        onChange={(e) => {
                                          const v = e.target.value;
                                          if (!v) return;
                                          const copy = [...visualSteps];
                                          if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                          if (!copy[stepIdx].actions[actIdx].extra!.headers) copy[stepIdx].actions[actIdx].extra!.headers = {};
                                          if (v === "bearer") copy[stepIdx].actions[actIdx].extra!.headers["Authorization"] = "Bearer ${inputs.apiToken}";
                                          if (v === "apikey") copy[stepIdx].actions[actIdx].extra!.headers["X-Api-Key"] = "${inputs.apiKey}";
                                          if (v === "basic") copy[stepIdx].actions[actIdx].extra!.headers["Authorization"] = "Basic ${inputs.basicAuth}";
                                          updateVisualSteps(copy);
                                        }}
                                      >
                                        <option value="">Auth Preset...</option>
                                        <option value="bearer">Bearer Token</option>
                                        <option value="apikey">API Key</option>
                                        <option value="basic">Basic Auth</option>
                                      </select>
                                      <label style={{ display: "flex", alignItems: "center", gap: "0.3rem", cursor: "pointer" }}>
                                        <input
                                          type="checkbox"
                                          checked={act.extra?.allowInsecure || false}
                                          onChange={(e) => {
                                            const copy = [...visualSteps];
                                            if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                            copy[stepIdx].actions[actIdx].extra!.allowInsecure = e.target.checked;
                                            updateVisualSteps(copy);
                                          }}
                                        /> Allow Insecure
                                      </label>
                                      <label style={{ display: "flex", alignItems: "center", gap: "0.3rem", cursor: "pointer" }}>
                                        <input
                                          type="checkbox"
                                          checked={act.extra?.continueOnError || false}
                                          onChange={(e) => {
                                            const copy = [...visualSteps];
                                            if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                            copy[stepIdx].actions[actIdx].extra!.continueOnError = e.target.checked;
                                            updateVisualSteps(copy);
                                          }}
                                        /> Continue on Error
                                      </label>
                                      <input
                                        type="text"
                                        className="form-control"
                                        style={{ width: "120px", fontSize: "0.8rem", padding: "0.2rem 0.5rem" }}
                                        placeholder="Register Variable"
                                        value={act.extra?.register || ""}
                                        onChange={(e) => {
                                          const copy = [...visualSteps];
                                          if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                          copy[stepIdx].actions[actIdx].extra!.register = e.target.value;
                                          updateVisualSteps(copy);
                                        }}
                                      />
                                      <input
                                        type="number"
                                        className="form-control"
                                        style={{ width: "80px", fontSize: "0.8rem", padding: "0.2rem 0.5rem" }}
                                        placeholder="Timeout (s)"
                                        value={act.extra?.timeoutSeconds || ""}
                                        onChange={(e) => {
                                          const copy = [...visualSteps];
                                          if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                          copy[stepIdx].actions[actIdx].extra!.timeoutSeconds = parseInt(e.target.value, 10);
                                          updateVisualSteps(copy);
                                        }}
                                      />
                                    </div>
                                  </div>
                                ) : ("""
content = content.replace('                                ) : (\n                                  <input\n                                    type={act.type', http_widget + '\n                                  <input\n                                    type={act.type')

with open('src/Seedarr.Frontend/src/pages/AutomationPage.tsx', 'w') as f:
    f.write(content)
