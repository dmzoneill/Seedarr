import sys

with open('src/Seedarr.Frontend/src/api/types.ts', 'r') as f:
    content = f.read()

new_props = """  shouldPause: boolean;
  shouldResume: boolean;
  shouldRemove: boolean;
  deleteDataOnRemove: boolean;
  shouldRecheck: boolean;
  shouldReannounce: boolean;
  shouldBoostTracker: boolean;"""

content = content.replace("  shouldPause: boolean;\n  shouldResume: boolean;\n  shouldRemove: boolean;\n  deleteDataOnRemove: boolean;", new_props)

with open('src/Seedarr.Frontend/src/api/types.ts', 'w') as f:
    f.write(content)

with open('src/Seedarr.Frontend/src/pages/AutomationPage.tsx', 'r') as f:
    content = f.read()

# Fix cleanUnwantedFiles types issue
content = content.replace('cleanUnwantedFiles', 'cleanFiles')
content = content.replace('deleteData: false', 'deleteData: undefined')
content = content.replace('act.deleteData', 'act.extra?.deleteData')
content = content.replace('actions[actIdx].deleteData', 'actions[actIdx].extra!.deleteData')

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
            
            let headers = act.extra.headers || {};
            if (act.extra.auth === "Bearer Token") headers["Authorization"] = "Bearer ${inputs.apiToken}";
            else if (act.extra.auth === "API Key (X-Api-Key)") headers["X-Api-Key"] = "${inputs.apiKey}";
            else if (act.extra.auth === "Basic Auth") headers["Authorization"] = "Basic ${inputs.basicAuth}";
            
            if (Object.keys(headers).length > 0) {
              yaml += `          headers:\\n`;
              for (const [k, v] of Object.entries(headers)) {
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

with open('src/Seedarr.Frontend/src/pages/AutomationPage.tsx', 'w') as f:
    f.write(content)
