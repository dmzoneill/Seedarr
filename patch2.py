import sys

with open('src/Seedarr.Frontend/src/pages/AutomationPage.tsx', 'r') as f:
    content = f.read()

yaml_code = """        } else if (act.type === "http") {
          yaml += `      - http:\\n`;
          if (act.extra?.method) yaml += `          method: '${act.extra.method}'\\n`;
          if (act.extra?.url || act.value) yaml += `          url: '${(act.extra?.url || act.value).replace(/'/g, "''")}'\\n`;
          
          let headers = act.extra?.headers || {};
          if (act.extra?.auth === "Bearer Token") headers["Authorization"] = "Bearer ${inputs.apiToken}";
          else if (act.extra?.auth === "API Key (X-Api-Key)") headers["X-Api-Key"] = "${inputs.apiKey}";
          else if (act.extra?.auth === "Basic Auth") headers["Authorization"] = "Basic ${inputs.basicAuth}";
          
          if (Object.keys(headers).length > 0) {
            yaml += `          headers:\\n`;
            for (const [k, v] of Object.entries(headers)) {
              yaml += `            ${k}: '${String(v).replace(/'/g, "''")}'\\n`;
            }
          }
          
          yaml += `          json: true\\n`;
          if (act.extra?.body) {
             const lines = act.extra.body.split('\\n');
             if (lines.length > 1) {
                 yaml += `          body: |\\n`;
                 for (const line of lines) {
                     yaml += `            ${line}\\n`;
                 }
             } else {
                 yaml += `          body: '${act.extra.body.replace(/'/g, "''")}'\\n`;
             }
          }
          if (act.extra?.timeout) yaml += `          timeoutSeconds: ${act.extra.timeout}\\n`;
          if (act.extra?.insecure) yaml += `          allowInsecure: true\\n`;
          if (act.extra?.continueOnError) yaml += `          continueOnError: true\\n`;
          if (act.extra?.register) yaml += `          register: '${act.extra.register}'\\n`;
        } else if (act.type === "pause") {"""
content = content.replace('        } else if (act.type === "pause") {', yaml_code, 1)

yaml_to_visual = """    } else if (currentStep && inActions && trimmed.startsWith("- http:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "http", value: "", extra: { method: "POST", url: "", body: "" } });
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
