import sys

with open('src/Seedarr.Frontend/src/pages/AutomationPage.tsx', 'r') as f:
    content = f.read()

# Add deleteData to VisualAction
content = content.replace(
    'extra?: Record<string, any>;\n}',
    'extra?: Record<string, any>;\n  deleteData?: boolean;\n}'
)

# Add http to visualStepsToYaml
yaml_code = """        } else if (act.type === "http") {
          if (!act.extra || Object.keys(act.extra).length === 0) {
            yaml += `      - http: '${act.value.replace(/'/g, "''")}'\\n`;
          } else {
            yaml += `      - http:\\n`;
            if (act.extra.method) yaml += `          method: '${act.extra.method}'\\n`;
            if (act.extra.url || act.value) yaml += `          url: '${(act.extra.url || act.value).replace(/'/g, "''")}'\\n`;
            if (act.extra.headers && Object.keys(act.extra.headers).length > 0) {
              yaml += `          headers:\\n`;
              for (const [k, v] of Object.entries(act.extra.headers)) {
                yaml += `            ${k}: '${String(v).replace(/'/g, "''")}'\\n`;
              }
            }
            if (act.extra.json) yaml += `          json: true\\n`;
            if (act.extra.body) yaml += `          body: '${act.extra.body.replace(/'/g, "''")}'\\n`;
            if (act.extra.timeoutSeconds) yaml += `          timeoutSeconds: ${act.extra.timeoutSeconds}\\n`;
            if (act.extra.allowInsecure) yaml += `          allowInsecure: true\\n`;
            if (act.extra.continueOnError) yaml += `          continueOnError: true\\n`;
            if (act.extra.register) yaml += `          register: '${act.extra.register}'\\n`;
          }
        } else if (act.type === "pause") {"""
content = content.replace('        } else if (act.type === "pause") {', yaml_code, 1)

# Add http parsing to yamlToVisualSteps
yaml_to_visual = """    } else if (currentStep && inActions && trimmed.startsWith("- http:")) {
      const v = trimmed.match(/- http:\\s*['"]?([^'"]+)['"]?/);
      if (v) {
        currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "http", value: v[1], extra: {} });
      } else {
        currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "http", value: "", extra: { method: "POST", url: "", json: true } });
      }
    } else if (currentStep && inActions && trimmed.startsWith("url:") && currentStep.actions.length > 0 && currentStep.actions[currentStep.actions.length - 1].type === "http") {
      const v = trimmed.match(/url:\\s*['"]?([^'"]+)['"]?/);
      if (v) {
        currentStep.actions[currentStep.actions.length - 1].value = v[1];
        if (!currentStep.actions[currentStep.actions.length - 1].extra) currentStep.actions[currentStep.actions.length - 1].extra = {};
        currentStep.actions[currentStep.actions.length - 1].extra!.url = v[1];
      }
    } else if (currentStep && inActions && trimmed.startsWith("method:") && currentStep.actions.length > 0 && currentStep.actions[currentStep.actions.length - 1].type === "http") {
      const v = trimmed.match(/method:\\s*['"]?([^'"]+)['"]?/);
      if (v) {
        if (!currentStep.actions[currentStep.actions.length - 1].extra) currentStep.actions[currentStep.actions.length - 1].extra = {};
        currentStep.actions[currentStep.actions.length - 1].extra!.method = v[1];
      }
    } else if (currentStep && inActions && trimmed.startsWith("body:") && currentStep.actions.length > 0 && currentStep.actions[currentStep.actions.length - 1].type === "http") {
      const v = trimmed.match(/body:\\s*['"]?([^'"]+)['"]?/);
      if (v) {
        if (!currentStep.actions[currentStep.actions.length - 1].extra) currentStep.actions[currentStep.actions.length - 1].extra = {};
        currentStep.actions[currentStep.actions.length - 1].extra!.body = v[1];
      }
    } else if (currentStep && inActions && trimmed.startsWith("register:") && currentStep.actions.length > 0 && currentStep.actions[currentStep.actions.length - 1].type === "http") {
      const v = trimmed.match(/register:\\s*['"]?([^'"]+)['"]?/);
      if (v) {
        if (!currentStep.actions[currentStep.actions.length - 1].extra) currentStep.actions[currentStep.actions.length - 1].extra = {};
        currentStep.actions[currentStep.actions.length - 1].extra!.register = v[1];
      }
    } else if (currentStep && inActions && trimmed.startsWith("- pause:")) {"""
content = content.replace('    } else if (currentStep && inActions && trimmed.startsWith("- pause:")) {', yaml_to_visual, 1)

# Fix cleanUnwantedFiles types issue
content = content.replace('cleanUnwantedFiles', 'cleanFiles')
content = content.replace('deleteData: false', 'deleteData: undefined')
content = content.replace('act.deleteData', 'act.extra?.deleteData')
content = content.replace('actions[actIdx].deleteData', 'actions[actIdx].extra!.deleteData')

# HTTP widget
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
                                    <div style={{ display: "flex", gap: "1rem", fontSize: "0.85rem", alignItems: "center" }}>
                                      <button
                                        type="button"
                                        className="btn btn-sm btn-outline"
                                        title="✨ Insert Torrent JSON Payload"
                                        onClick={(e) => {
                                          e.preventDefault();
                                          const copy = [...visualSteps];
                                          if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                          copy[stepIdx].actions[actIdx].extra!.body = '{"event": "complete", "torrent": "${torrent.name}", "size": ${torrent.size}, "hash": "${torrent.infoHash}"}';
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
                                ) : (
                                  <input"""
content = content.replace('                                ) : (\n                                  <input', http_widget)

with open('src/Seedarr.Frontend/src/pages/AutomationPage.tsx', 'w') as f:
    f.write(content)
